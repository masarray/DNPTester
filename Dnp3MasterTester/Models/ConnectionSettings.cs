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
