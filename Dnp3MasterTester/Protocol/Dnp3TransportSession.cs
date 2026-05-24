using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using Dnp3MasterTester.Models;

namespace Dnp3MasterTester.Protocol;

internal sealed class Dnp3TransportSession : IAsyncDisposable
{
    private readonly ConnectionSettings _settings;
    private readonly Action<string, string, string> _trace;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Stream? _stream;
    private TcpClient? _tcpClient;
    private SerialPort? _serialPort;
    private bool _frameCountBit;
    private byte _transportSequence;
    private byte _appSequence;

    public Dnp3TransportSession(ConnectionSettings settings, Action<string, string, string> trace)
    {
        _settings = settings;
        _trace = trace;
    }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_settings.Transport == DnpTransportType.Serial)
        {
            _serialPort = new SerialPort(
                _settings.SerialPort,
                checked((int)_settings.SerialBaudRate),
                MapParity(_settings.SerialParity),
                (int)_settings.SerialDataBits,
                MapStopBits(_settings.SerialStopBits))
            {
                Handshake = MapHandshake(_settings.SerialFlowControl),
                ReadTimeout = Math.Max(1000, _settings.RequestTimeoutSeconds * 1000),
                WriteTimeout = Math.Max(1000, _settings.RequestTimeoutSeconds * 1000)
            };
            _serialPort.Open();
            _stream = _serialPort.BaseStream;
            _trace("CHANNEL", "Serial", $"Opened {_settings.GetSerialSummary()}");
            return;
        }

        var (host, port) = ParseEndpoint(_settings.Endpoint);
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, cancellationToken);
        _stream = _tcpClient.GetStream();
        _trace("CHANNEL", "TcpClient", $"Connected to {_settings.Endpoint}");
    }

    public async Task ResetLinkAsync(CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            await SendLinkFrameAsync(Dnp3LinkLayer.FuncResetLinkStates, [], cancellationToken);
            var frame = await ReadFrameAsync(cancellationToken);
            _trace("RX", "Link", $"Reset link response control=0x{frame.Control:X2} func={frame.Function}");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task<Dnp3ApplicationResponse> CheckLinkStatusAsync(CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            await SendLinkFrameAsync(Dnp3LinkLayer.FuncRequestLinkStatus, [], cancellationToken);
            var frame = await ReadFrameAsync(cancellationToken);
            _trace("RX", "Link", $"Link status response control=0x{frame.Control:X2} func={frame.Function}");
            return new Dnp3ApplicationResponse(0, 0, 0, [], []);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task<Dnp3ApplicationResponse> ReadAsync(bool class0, bool class1, bool class2, bool class3, CancellationToken cancellationToken)
    {
        var request = Dnp3Application.BuildReadRequest(NextAppSequence(), class0, class1, class2, class3);
        return await SendApplicationRequestAsync(request, expectResponse: true, cancellationToken);
    }

    public async Task<Dnp3ApplicationResponse> OperateBinaryAsync(ushort index, CommandMode mode, OpType operation, CancellationToken cancellationToken)
    {
        if (mode == CommandMode.SelectBeforeOperate)
        {
            var select = Dnp3Application.BuildBinaryCommand(NextAppSequence(), Dnp3Application.FunctionSelect, index, operation);
            var selectResponse = await SendApplicationRequestAsync(select, expectResponse: true, cancellationToken);
            if (selectResponse.CommandStatuses.Any(x => !string.Equals(x.Status, "Success", StringComparison.OrdinalIgnoreCase)))
            {
                return selectResponse;
            }

            var operate = Dnp3Application.BuildBinaryCommand(NextAppSequence(), Dnp3Application.FunctionOperate, index, operation);
            return await SendApplicationRequestAsync(operate, expectResponse: true, cancellationToken);
        }

        var direct = Dnp3Application.BuildBinaryCommand(NextAppSequence(), Dnp3Application.FunctionDirectOperate, index, operation);
        return await SendApplicationRequestAsync(direct, expectResponse: true, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _serialPort?.Dispose();
        _requestLock.Dispose();
        _sendLock.Dispose();
        await Task.CompletedTask;
    }

    private async Task<Dnp3ApplicationResponse> SendApplicationRequestAsync(byte[] appPayload, bool expectResponse, CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var transport = AddTransportHeader(appPayload);
            await SendLinkFrameAsync(Dnp3LinkLayer.FuncConfirmedUserData, transport, cancellationToken);
            if (!expectResponse)
            {
                return new Dnp3ApplicationResponse(0, 0, 0, [], []);
            }

            while (true)
            {
                var frame = await ReadFrameAsync(cancellationToken);
                if (frame.Payload.Length == 0)
                {
                    _trace("RX", "Link", $"Received link response control=0x{frame.Control:X2} func={frame.Function}");
                    continue;
                }

                var response = Dnp3Application.Decode(RemoveTransportHeaders(frame.Payload));
                _trace("RX", "Application", $"Function=0x{response.FunctionCode:X2} seq={response.Sequence} iin=0x{response.InternalIndications:X4} objects={response.Measurements.Count}");
                if (response.FunctionCode == Dnp3Application.FunctionUnsolicitedResponse)
                {
                    await ConfirmAsync(response.Sequence, cancellationToken);
                }

                return response;
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task ConfirmAsync(byte appSequence, CancellationToken cancellationToken)
    {
        var confirm = AddTransportHeader(Dnp3Application.BuildConfirm(appSequence));
        await SendLinkFrameAsync(Dnp3LinkLayer.FuncUnconfirmedUserData, confirm, cancellationToken);
        _trace("TX", "Application", $"Confirmed unsolicited response seq={appSequence}");
    }

    private async Task SendLinkFrameAsync(byte function, byte[] payload, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("DNP3 transport is not open.");
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var frame = Dnp3LinkLayer.EncodePrimary(_settings.MasterAddress, _settings.OutstationAddress, function, payload, _frameCountBit);
            _frameCountBit = !_frameCountBit;
            await _stream.WriteAsync(frame, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
            _trace("TX", "Link", $"Sent func=0x{function:X2} bytes={frame.Length}");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<Dnp3LinkFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("DNP3 transport is not open.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.RequestTimeoutSeconds)));
        var token = timeoutCts.Token;
        var buffer = new List<byte>(292);

        while (true)
        {
            var value = await ReadByteAsync(_stream, token);
            if (buffer.Count == 0 && value != Dnp3LinkLayer.Start1)
            {
                continue;
            }

            if (buffer.Count == 1 && value != Dnp3LinkLayer.Start2)
            {
                buffer.Clear();
                continue;
            }

            buffer.Add(value);
            var expectedLength = Dnp3LinkLayer.GetEncodedLength(buffer.ToArray());
            if (expectedLength > 0 && buffer.Count >= expectedLength)
            {
                if (!Dnp3LinkLayer.TryDecode(buffer.ToArray(), out var frame))
                {
                    throw new InvalidDataException("Received DNP3 frame with invalid CRC or structure.");
                }

                return frame;
            }
        }
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException("DNP3 transport closed.");
        }

        return buffer[0];
    }

    private byte[] AddTransportHeader(byte[] appPayload)
    {
        var sequence = _transportSequence++ & 0x3F;
        var payload = new byte[appPayload.Length + 1];
        payload[0] = (byte)(0xC0 | sequence);
        appPayload.CopyTo(payload, 1);
        return payload;
    }

    private static byte[] RemoveTransportHeaders(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return [];
        }

        var result = new List<byte>(payload.Length);
        var offset = 0;
        while (offset < payload.Length)
        {
            result.AddRange(payload.Skip(offset + 1).Take(Math.Min(249, payload.Length - offset - 1)));
            break;
        }

        return result.ToArray();
    }

    private byte NextAppSequence()
    {
        var value = _appSequence;
        _appSequence = (byte)((_appSequence + 1) & 0x0F);
        return value;
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        var index = endpoint.LastIndexOf(':');
        if (index <= 0 || index == endpoint.Length - 1)
        {
            throw new FormatException("Remote endpoint must use host:port format.");
        }

        return (endpoint[..index], int.Parse(endpoint[(index + 1)..]));
    }

    private static System.IO.Ports.Parity MapParity(Models.Parity parity) => parity switch
    {
        Models.Parity.Odd => System.IO.Ports.Parity.Odd,
        Models.Parity.Even => System.IO.Ports.Parity.Even,
        Models.Parity.Mark => System.IO.Ports.Parity.Mark,
        Models.Parity.Space => System.IO.Ports.Parity.Space,
        _ => System.IO.Ports.Parity.None
    };

    private static System.IO.Ports.StopBits MapStopBits(Models.StopBits stopBits) => stopBits switch
    {
        Models.StopBits.OnePointFive => System.IO.Ports.StopBits.OnePointFive,
        Models.StopBits.Two => System.IO.Ports.StopBits.Two,
        _ => System.IO.Ports.StopBits.One
    };

    private static Handshake MapHandshake(FlowControl flowControl) => flowControl switch
    {
        FlowControl.XonXoff => Handshake.XOnXOff,
        FlowControl.RequestToSend => Handshake.RequestToSend,
        FlowControl.RequestToSendXonXoff => Handshake.RequestToSendXOnXOff,
        _ => Handshake.None
    };
}
