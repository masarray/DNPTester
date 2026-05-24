using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dnp3MasterTester.Models;

public enum DnpTransportType
{
    Tcp,
    Serial
}

public sealed class ConnectionSettings : INotifyPropertyChanged
{
    private DnpTransportType _transport = DnpTransportType.Tcp;
    private PollingProfileKind _pollingProfile = PollingProfileKind.BalancedScada;
    private string _endpoint = "127.0.0.1:20000";
    private string _serialPort = "COM1";
    private uint _serialBaudRate = 9600;
    private DataBits _serialDataBits = DataBits.Eight;
    private StopBits _serialStopBits = StopBits.One;
    private Parity _serialParity = Parity.None;
    private FlowControl _serialFlowControl = FlowControl.None;
    private int _serialOpenRetrySeconds = 5;
    private ushort _masterAddress = 1;
    private ushort _outstationAddress = 1024;
    private int _requestTimeoutSeconds = 5;
    private int _eventPollSeconds = 1;
    private int _staticRefreshSeconds = 60;
    private bool _enableAutoEventScan = true;
    private bool _enableUnsolicited;
    private bool _enableStartupIntegrity = true;
    private bool _enableSlowStaticRefresh = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PollingProfileDefinition GetEffectivePollingProfile()
    {
        var fastEventSeconds = EventPollSeconds > 0 ? EventPollSeconds : 1;
        var staticRefreshSeconds = StaticRefreshSeconds > 0 ? StaticRefreshSeconds : 60;
        var keepAliveTimeout = TimeSpan.FromSeconds(Math.Max(30, RequestTimeoutSeconds * 6));

        return PollingProfile switch
        {
            PollingProfileKind.StrictEvent => new PollingProfileDefinition(
                PollingProfile,
                fastEventSeconds,
                0,
                false,
                EnableAutoEventScan,
                EnableUnsolicited,
                EnableStartupIntegrity,
                keepAliveTimeout),
            PollingProfileKind.RelayFatInteroperability => new PollingProfileDefinition(
                PollingProfile,
                fastEventSeconds,
                StaticRefreshSeconds > 0 ? StaticRefreshSeconds : 30,
                EnableSlowStaticRefresh,
                EnableAutoEventScan,
                EnableUnsolicited,
                EnableStartupIntegrity,
                keepAliveTimeout),
            _ => new PollingProfileDefinition(
                PollingProfile,
                fastEventSeconds,
                staticRefreshSeconds,
                EnableSlowStaticRefresh,
                EnableAutoEventScan,
                EnableUnsolicited,
                EnableStartupIntegrity,
                keepAliveTimeout)
        };
    }

    public DnpTransportType Transport
    {
        get => _transport;
        set => SetProperty(ref _transport, value);
    }

    public PollingProfileKind PollingProfile
    {
        get => _pollingProfile;
        set => SetProperty(ref _pollingProfile, value);
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

    public int SerialOpenRetrySeconds
    {
        get => _serialOpenRetrySeconds;
        set => SetProperty(ref _serialOpenRetrySeconds, value);
    }

    public ushort MasterAddress
    {
        get => _masterAddress;
        set => SetProperty(ref _masterAddress, value);
    }

    public ushort OutstationAddress
    {
        get => _outstationAddress;
        set => SetProperty(ref _outstationAddress, value);
    }

    public int RequestTimeoutSeconds
    {
        get => _requestTimeoutSeconds;
        set => SetProperty(ref _requestTimeoutSeconds, value);
    }

    public int EventPollSeconds
    {
        get => _eventPollSeconds;
        set => SetProperty(ref _eventPollSeconds, value);
    }

    public int StaticRefreshSeconds
    {
        get => _staticRefreshSeconds;
        set => SetProperty(ref _staticRefreshSeconds, value);
    }

    public bool EnableAutoEventScan
    {
        get => _enableAutoEventScan;
        set => SetProperty(ref _enableAutoEventScan, value);
    }

    public bool EnableUnsolicited
    {
        get => _enableUnsolicited;
        set => SetProperty(ref _enableUnsolicited, value);
    }

    public bool EnableStartupIntegrity
    {
        get => _enableStartupIntegrity;
        set => SetProperty(ref _enableStartupIntegrity, value);
    }

    public bool EnableSlowStaticRefresh
    {
        get => _enableSlowStaticRefresh;
        set => SetProperty(ref _enableSlowStaticRefresh, value);
    }

    public TimeSpan GetSerialOpenRetryDelay()
    {
        return TimeSpan.FromSeconds(Math.Max(1, SerialOpenRetrySeconds));
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

        if (RequestTimeoutSeconds <= 0)
        {
            errors.Add("Request timeout must be greater than 0 seconds.");
        }

        if (EventPollSeconds <= 0)
        {
            errors.Add("Fast event poll must be greater than 0 seconds.");
        }

        if (EnableSlowStaticRefresh && StaticRefreshSeconds <= 0)
        {
            errors.Add("Static refresh must be greater than 0 seconds when slow static refresh is enabled.");
        }

        switch (Transport)
        {
            case DnpTransportType.Tcp:
                if (string.IsNullOrWhiteSpace(Endpoint))
                {
                    errors.Add("Remote endpoint is required for TCP transport.");
                }
                else if (!Endpoint.Contains(':', StringComparison.Ordinal))
                {
                    errors.Add("Remote endpoint must use host:port format for TCP transport.");
                }
                break;

            case DnpTransportType.Serial:
                if (string.IsNullOrWhiteSpace(SerialPort))
                {
                    errors.Add("Serial port is required for serial transport.");
                }

                if (SerialBaudRate == 0)
                {
                    errors.Add("Serial baud rate must be greater than 0.");
                }

                if (SerialOpenRetrySeconds <= 0)
                {
                    errors.Add("Serial port open retry must be greater than 0 seconds.");
                }
                break;
        }

        return errors;
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
