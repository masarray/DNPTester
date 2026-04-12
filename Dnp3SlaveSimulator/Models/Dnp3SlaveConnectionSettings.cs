using Dnp3SlaveSimulator.ViewModels;

namespace Dnp3SlaveSimulator.Models;

public sealed class Dnp3SlaveConnectionSettings : ViewModelBase
{
    private Dnp3SlaveTransportType _transport = Dnp3SlaveTransportType.TcpServer;
    private string _endpoint = "127.0.0.1:20000";
    private string _serialPort = "COM1";
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
}
