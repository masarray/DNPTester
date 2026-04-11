using System.Collections.ObjectModel;
using System.Windows;
using Dnp3MasterTester.Models;
using Dnp3MasterTester.Services;
using dnp3;

namespace Dnp3MasterTester.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxRows = 500;
    private readonly IDnp3MasterService _service;

    private string _connectionState = "Idle";
    private string _connectionDetail = "Disconnected";
    private bool _isBusy;

    public MainViewModel()
        : this(new Dnp3MasterService())
    {
    }

    public MainViewModel(IDnp3MasterService service)
    {
        _service = service;
        Settings = new ConnectionSettings();
        TransportTypes = Enum.GetValues(typeof(DnpTransportType)).Cast<DnpTransportType>().ToArray();
        PollingProfiles = Enum.GetValues(typeof(PollingProfileKind)).Cast<PollingProfileKind>().ToArray();
        SerialDataBitOptions = Enum.GetValues(typeof(DataBits)).Cast<DataBits>().ToArray();
        SerialStopBitOptions = Enum.GetValues(typeof(StopBits)).Cast<StopBits>().ToArray();
        SerialParityOptions = Enum.GetValues(typeof(Parity)).Cast<Parity>().ToArray();
        SerialFlowControlOptions = Enum.GetValues(typeof(FlowControl)).Cast<FlowControl>().ToArray();
        Settings.PropertyChanged += (_, _) => RaiseConnectionSummaryChanged();

        ConnectCommand = new RelayCommand(_ => ConnectAsync(), _ => !_isBusy && !_service.IsConnected);
        DisconnectCommand = new RelayCommand(_ => DisconnectAsync(), _ => !_isBusy && _service.IsConnected);
        IntegrityPollCommand = new RelayCommand(_ => IntegrityPollAsync(), _ => !_isBusy && _service.IsConnected);
        EventPollCommand = new RelayCommand(_ => EventPollAsync(), _ => !_isBusy && _service.IsConnected);
        LinkStatusCommand = new RelayCommand(_ => CheckLinkAsync(), _ => !_isBusy && _service.IsConnected);

        _service.ConnectionStateChanged += (_, snapshot) => Dispatch(() =>
        {
            ConnectionState = snapshot.State;
            ConnectionDetail = snapshot.Detail;
        });
        _service.EventLogReceived += (_, entry) => Dispatch(() => InsertTop(EventLogs, entry));
        _service.LinkTraceReceived += (_, entry) => Dispatch(() => InsertTop(LinkTrace, entry));
        _service.SoeEventReceived += (_, row) => Dispatch(() => InsertTop(SoeAudit, row));
        _service.ValueReceived += (_, row) => Dispatch(() => UpsertValue(row));
    }

    public ConnectionSettings Settings { get; }
    public IReadOnlyList<DnpTransportType> TransportTypes { get; }
    public IReadOnlyList<PollingProfileKind> PollingProfiles { get; }
    public IReadOnlyList<DataBits> SerialDataBitOptions { get; }
    public IReadOnlyList<StopBits> SerialStopBitOptions { get; }
    public IReadOnlyList<Parity> SerialParityOptions { get; }
    public IReadOnlyList<FlowControl> SerialFlowControlOptions { get; }
    public ObservableCollection<ValueViewerRow> ValueViewer { get; } = new();
    public ObservableCollection<EventLogEntry> EventLogs { get; } = new();
    public ObservableCollection<SoeEventRow> SoeAudit { get; } = new();
    public ObservableCollection<LinkTraceEntry> LinkTrace { get; } = new();

    public string ConnectionProfile => $"{Settings.Transport} / Master {Settings.MasterAddress} / Outstation {Settings.OutstationAddress}";
    public string ConnectionTarget => Settings.Transport == DnpTransportType.Serial ? Settings.GetSerialSummary() : Settings.Endpoint;
    public string PollingProfile => Settings.GetEffectivePollingProfile().Summary;
    public string SerialProfile => Settings.GetSerialSummary();
    public bool IsTcpTransport => Settings.Transport == DnpTransportType.Tcp;
    public bool IsSerialTransport => Settings.Transport == DnpTransportType.Serial;

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand IntegrityPollCommand { get; }
    public RelayCommand EventPollCommand { get; }
    public RelayCommand LinkStatusCommand { get; }

    public string ConnectionState
    {
        get => _connectionState;
        private set => SetProperty(ref _connectionState, value);
    }

    public string ConnectionDetail
    {
        get => _connectionDetail;
        private set => SetProperty(ref _connectionDetail, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandState();
            }
        }
    }

    private Task ConnectAsync() => RunBusyAsync(async () =>
    {
        var validationErrors = Settings.Validate();
        if (validationErrors.Count != 0)
        {
            var detail = string.Join(" ", validationErrors);
            ConnectionState = "Invalid Settings";
            ConnectionDetail = detail;
            InsertTop(EventLogs, new EventLogEntry
            {
                TimestampLocal = DateTime.Now,
                EventType = "Validation Error",
                Source = "UI",
                Detail = detail,
                Status = "Blocked"
            });
            return;
        }

        await _service.ConnectAsync(Settings);
    });
    private Task DisconnectAsync() => RunBusyAsync(_service.DisconnectAsync);
    private Task IntegrityPollAsync() => RunBusyAsync(_service.RunIntegrityPollAsync);
    private Task EventPollAsync() => RunBusyAsync(_service.DemandEventPollAsync);
    private Task CheckLinkAsync() => RunBusyAsync(_service.CheckLinkStatusAsync);

    private async Task RunBusyAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            InsertTop(EventLogs, new EventLogEntry
            {
                TimestampLocal = DateTime.Now,
                EventType = "UI Error",
                Source = "UI",
                Detail = ex.Message,
                Status = "Error"
            });
        }
        finally
        {
            IsBusy = false;
            RaiseCommandState();
        }
    }

    private void UpsertValue(ValueViewerRow row)
    {
        var existing = ValueViewer.FirstOrDefault(x => x.PointType == row.PointType && x.Index == row.Index);
        if (existing is null)
        {
            InsertTop(ValueViewer, row);
            return;
        }

        existing.Value = row.Value;
        existing.Flags = row.Flags;
        existing.Quality = row.Quality;
        existing.ReceivedAtLocal = row.ReceivedAtLocal;
        existing.SourceTimestampLocal = row.SourceTimestampLocal;
        existing.SourceTimestampKind = row.SourceTimestampKind;
        existing.Source = row.Source;
        existing.SourceReason = row.SourceReason;

        var index = ValueViewer.IndexOf(existing);
        if (index > 0)
        {
            ValueViewer.Move(index, 0);
        }
    }

    private static void InsertTop<T>(ObservableCollection<T> items, T item)
    {
        items.Insert(0, item);
        while (items.Count > MaxRows)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private static void Dispatch(Action action)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(action);
    }

    private void RaiseCommandState()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        IntegrityPollCommand.RaiseCanExecuteChanged();
        EventPollCommand.RaiseCanExecuteChanged();
        LinkStatusCommand.RaiseCanExecuteChanged();
    }

    private void RaiseConnectionSummaryChanged()
    {
        RaisePropertyChanged(nameof(ConnectionProfile));
        RaisePropertyChanged(nameof(ConnectionTarget));
        RaisePropertyChanged(nameof(PollingProfile));
        RaisePropertyChanged(nameof(SerialProfile));
        RaisePropertyChanged(nameof(IsTcpTransport));
        RaisePropertyChanged(nameof(IsSerialTransport));
    }
}
