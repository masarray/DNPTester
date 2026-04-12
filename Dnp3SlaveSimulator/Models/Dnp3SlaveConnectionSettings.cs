using Dnp3SlaveSimulator.ViewModels;
using dnp3;

namespace Dnp3SlaveSimulator.Models;

public sealed class Dnp3SlaveConnectionSettings : ViewModelBase
{
    private Dnp3SlaveTransportType _transport = Dnp3SlaveTransportType.TcpServer;
    private string _endpoint = "127.0.0.1:20000";
    private string _serialPort = "COM1";
    private uint _serialBaudRate = 9600;
    private DataBits _serialDataBits = DataBits.Eight;
    private StopBits _serialStopBits = StopBits.One;
    private Parity _serialParity = Parity.None;
    private FlowControl _serialFlowControl = FlowControl.None;
    private ushort _outstationAddress = 1024;
    private ushort _masterAddress = 1;
    private int _portRetrySeconds = 5;
    private bool _enableUnsolicited;
    private bool _unsolicitedClass1 = true;
    private bool _unsolicitedClass2;
    private bool _unsolicitedClass3;

    public Dnp3SlaveTransportType Transport
    {
        get => _transport;
        set => SetProperty(ref _transport, value);
    }

    public string Endpoint
    {
        get => _endpoint;
        set => SetProperty(ref _endpoint, value);
    }

    public string SerialPort
    {
        get => _serialPort;
        set => SetProperty(ref _serialPort, value);
    }

    public ushort OutstationAddress
    {
        get => _outstationAddress;
        set => SetProperty(ref _outstationAddress, value);
    }

    public uint SerialBaudRate
    {
        get => _serialBaudRate;
        set => SetProperty(ref _serialBaudRate, value);
    }

    public DataBits SerialDataBits
    {
        get => _serialDataBits;
        set => SetProperty(ref _serialDataBits, value);
    }

    public StopBits SerialStopBits
    {
        get => _serialStopBits;
        set => SetProperty(ref _serialStopBits, value);
    }

    public Parity SerialParity
    {
        get => _serialParity;
        set => SetProperty(ref _serialParity, value);
    }

    public FlowControl SerialFlowControl
    {
        get => _serialFlowControl;
        set => SetProperty(ref _serialFlowControl, value);
    }

    public ushort MasterAddress
    {
        get => _masterAddress;
        set => SetProperty(ref _masterAddress, value);
    }

    public int PortRetrySeconds
    {
        get => _portRetrySeconds;
        set => SetProperty(ref _portRetrySeconds, value);
    }

    public bool EnableUnsolicited
    {
        get => _enableUnsolicited;
        set => SetProperty(ref _enableUnsolicited, value);
    }

    public bool UnsolicitedClass1
    {
        get => _unsolicitedClass1;
        set => SetProperty(ref _unsolicitedClass1, value);
    }

    public bool UnsolicitedClass2
    {
        get => _unsolicitedClass2;
        set => SetProperty(ref _unsolicitedClass2, value);
    }

    public bool UnsolicitedClass3
    {
        get => _unsolicitedClass3;
        set => SetProperty(ref _unsolicitedClass3, value);
    }

    public string GetSerialSummary()
    {
        return $"{SerialPort} @ {SerialBaudRate} {SerialDataBits}/{SerialParity}/{SerialStopBits} Flow={SerialFlowControl}";
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MasterAddress == 0)
        {
            errors.Add("Master address must be greater than 0.");
        }

        if (OutstationAddress == 0)
        {
            errors.Add("Outstation address must be greater than 0.");
        }

        if (Transport == Dnp3SlaveTransportType.TcpServer)
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                errors.Add("TCP endpoint is required.");
            }
            else if (!Endpoint.Contains(':', StringComparison.Ordinal))
            {
                errors.Add("TCP endpoint must use host:port format.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(SerialPort))
            {
                errors.Add("Serial port is required.");
            }

            if (SerialBaudRate == 0)
            {
                errors.Add("Serial baud rate must be greater than 0.");
            }

            if (PortRetrySeconds <= 0)
            {
                errors.Add("Serial port retry must be greater than 0 seconds.");
            }
        }

        return errors;
    }
}
