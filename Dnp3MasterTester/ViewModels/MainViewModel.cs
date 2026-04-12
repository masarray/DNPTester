using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Dnp3MasterTester.Models;
using Dnp3MasterTester.Services;
using dnp3;

namespace Dnp3MasterTester.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxRows = 500;
    private readonly IDnp3MasterService _service;
    private readonly Dictionary<string, PointCatalogEntry> _pointCatalog = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _profileSerializerOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private string _connectionState = "Idle";
    private string _connectionDetail = "Disconnected";
    private bool _isBusy;
    private PointCatalogProfile? _selectedPointCatalogProfile;
    private PointCatalogEntry? _selectedPointCatalogEntry;
    private ushort _commandPointIndex;
    private CommandMode _selectedCommandMode = CommandMode.DirectOperate;
    private OpType _selectedBinaryOperation = OpType.LatchOn;
    private CommandTransaction? _latestCommandTransaction;

    public MainViewModel()
        : this(new Dnp3MasterService())
    {
    }

    public MainViewModel(IDnp3MasterService service)
    {
        _service = service;
        Settings = new ConnectionSettings();
        PointCatalogProfiles = LoadPointCatalogProfiles();
        PointCatalog = new ObservableCollection<PointCatalogEntry>();
        TransportTypes = Enum.GetValues(typeof(DnpTransportType)).Cast<DnpTransportType>().ToArray();
        PollingProfiles = Enum.GetValues(typeof(PollingProfileKind)).Cast<PollingProfileKind>().ToArray();
        PointTypeOptions = ["Binary Input", "Binary Output", "Binary Output Status", "Analog Input", "Analog Output Status"];
        SerialDataBitOptions = Enum.GetValues(typeof(DataBits)).Cast<DataBits>().ToArray();
        SerialStopBitOptions = Enum.GetValues(typeof(StopBits)).Cast<StopBits>().ToArray();
        SerialParityOptions = Enum.GetValues(typeof(Parity)).Cast<Parity>().ToArray();
        SerialFlowControlOptions = Enum.GetValues(typeof(FlowControl)).Cast<FlowControl>().ToArray();
        CommandModes = Enum.GetValues(typeof(CommandMode)).Cast<CommandMode>().ToArray();
        BinaryOperations = new[] { OpType.LatchOn, OpType.LatchOff, OpType.PulseOn, OpType.PulseOff };
        Settings.PropertyChanged += (_, _) => RaiseConnectionSummaryChanged();

        ConnectCommand = new RelayCommand(_ => ConnectAsync(), _ => !_isBusy && !_service.IsConnected);
        DisconnectCommand = new RelayCommand(_ => DisconnectAsync(), _ => !_isBusy && _service.IsConnected);
        IntegrityPollCommand = new RelayCommand(_ => IntegrityPollAsync(), _ => !_isBusy && _service.IsConnected);
        EventPollCommand = new RelayCommand(_ => EventPollAsync(), _ => !_isBusy && _service.IsConnected);
        LinkStatusCommand = new RelayCommand(_ => CheckLinkAsync(), _ => !_isBusy && _service.IsConnected);
        SendBinaryCommand = new RelayCommand(_ => SendBinaryCommandAsync(), _ => !_isBusy && _service.IsConnected);
        AddPointCatalogEntryCommand = new RelayCommand(_ => AddPointCatalogEntry(), _ => SelectedPointCatalogProfile is not null);
        RemovePointCatalogEntryCommand = new RelayCommand(_ => RemovePointCatalogEntry(), _ => SelectedPointCatalogEntry is not null);
        SavePointCatalogProfileCommand = new RelayCommand(_ => SavePointCatalogProfile(), _ => SelectedPointCatalogProfile is not null);
        ReloadPointCatalogProfileCommand = new RelayCommand(_ => ReloadPointCatalogProfile(), _ => SelectedPointCatalogProfile is not null);
        SelectedPointCatalogProfile = PointCatalogProfiles.FirstOrDefault();

        _service.ConnectionStateChanged += (_, snapshot) => Dispatch(() =>
        {
            ConnectionState = snapshot.State;
            ConnectionDetail = snapshot.Detail;
        });
        _service.CommandTransactionUpdated += (_, transaction) => Dispatch(() =>
        {
            LatestCommandTransaction = Enrich(transaction);
            ReplaceLifecycle(transaction.Lifecycle);
        });
        _service.EventLogReceived += (_, entry) => Dispatch(() => InsertTop(EventLogs, Enrich(entry)));
        _service.LinkTraceReceived += (_, entry) => Dispatch(() => InsertTop(LinkTrace, entry));
        _service.SoeEventReceived += (_, row) => Dispatch(() => InsertTop(SoeAudit, Enrich(row)));
        _service.ValueReceived += (_, row) => Dispatch(() => UpsertValue(Enrich(row)));
    }

    public ConnectionSettings Settings { get; }
    public ObservableCollection<PointCatalogProfile> PointCatalogProfiles { get; }
    public ObservableCollection<PointCatalogEntry> PointCatalog { get; }
    public IReadOnlyList<DnpTransportType> TransportTypes { get; }
    public IReadOnlyList<PollingProfileKind> PollingProfiles { get; }
    public IReadOnlyList<string> PointTypeOptions { get; }
    public IReadOnlyList<DataBits> SerialDataBitOptions { get; }
    public IReadOnlyList<StopBits> SerialStopBitOptions { get; }
    public IReadOnlyList<Parity> SerialParityOptions { get; }
    public IReadOnlyList<FlowControl> SerialFlowControlOptions { get; }
    public IReadOnlyList<CommandMode> CommandModes { get; }
    public IReadOnlyList<OpType> BinaryOperations { get; }
    public ObservableCollection<ValueViewerRow> ValueViewer { get; } = new();
    public ObservableCollection<CommandLifecycleEntry> CommandLifecycle { get; } = new();
    public ObservableCollection<EventLogEntry> EventLogs { get; } = new();
    public ObservableCollection<SoeEventRow> SoeAudit { get; } = new();
    public ObservableCollection<LinkTraceEntry> LinkTrace { get; } = new();

    public string ConnectionProfile => $"{Settings.Transport} / Master {Settings.MasterAddress} / Outstation {Settings.OutstationAddress}";
    public string PointCatalogProfileName => SelectedPointCatalogProfile?.Name ?? "No Point Profile";
    public string ConnectionTarget => Settings.Transport == DnpTransportType.Serial ? Settings.GetSerialSummary() : Settings.Endpoint;
    public string PollingProfile => Settings.GetEffectivePollingProfile().Summary;
    public string SerialProfile => Settings.GetSerialSummary();
    public bool IsTcpTransport => Settings.Transport == DnpTransportType.Tcp;
    public bool IsSerialTransport => Settings.Transport == DnpTransportType.Serial;
    public string LatestTransactionId => LatestCommandTransaction?.TransactionId ?? "-";
    public string LatestTransactionPoint => LatestCommandTransaction?.PointLabel ?? "-";
    public string LatestAcceptanceResult => LatestCommandTransaction?.AcceptanceResult ?? "Pending";
    public string LatestFeedbackResult => LatestCommandTransaction?.FeedbackResult ?? "Pending";
    public string LatestFeedbackEvidence => LatestCommandTransaction?.FeedbackEvidenceText ?? "-";
    public string LatestFinalVerdict => LatestCommandTransaction?.FinalVerdict ?? "Idle";
    public string LatestPreparedAt => LatestCommandTransaction?.PreparedAtText ?? "-";
    public string LatestRequestedAt => LatestCommandTransaction?.RequestedAtText ?? "-";
    public string LatestAcceptanceAt => LatestCommandTransaction?.AcceptanceAtText ?? "-";
    public string LatestFeedbackAt => LatestCommandTransaction?.FeedbackAtText ?? "-";
    public string LatestAcceptanceLatency => LatestCommandTransaction?.AcceptanceLatencyText ?? "-";
    public string LatestFeedbackLatency => LatestCommandTransaction?.FeedbackLatencyText ?? "-";
    public string LatestFeedbackMatch => LatestCommandTransaction?.FeedbackMatchedText ?? "No";

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand IntegrityPollCommand { get; }
    public RelayCommand EventPollCommand { get; }
    public RelayCommand LinkStatusCommand { get; }
    public RelayCommand SendBinaryCommand { get; }
    public RelayCommand AddPointCatalogEntryCommand { get; }
    public RelayCommand RemovePointCatalogEntryCommand { get; }
    public RelayCommand SavePointCatalogProfileCommand { get; }
    public RelayCommand ReloadPointCatalogProfileCommand { get; }

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

    public ushort CommandPointIndex
    {
        get => _commandPointIndex;
        set => SetProperty(ref _commandPointIndex, value);
    }

    public CommandMode SelectedCommandMode
    {
        get => _selectedCommandMode;
        set => SetProperty(ref _selectedCommandMode, value);
    }

    public OpType SelectedBinaryOperation
    {
        get => _selectedBinaryOperation;
        set => SetProperty(ref _selectedBinaryOperation, value);
    }

    public CommandTransaction? LatestCommandTransaction
    {
        get => _latestCommandTransaction;
        private set
        {
            if (SetProperty(ref _latestCommandTransaction, value))
            {
                RaisePropertyChanged(nameof(LatestTransactionId));
                RaisePropertyChanged(nameof(LatestTransactionPoint));
                RaisePropertyChanged(nameof(LatestAcceptanceResult));
                RaisePropertyChanged(nameof(LatestFeedbackResult));
                RaisePropertyChanged(nameof(LatestFeedbackEvidence));
                RaisePropertyChanged(nameof(LatestFinalVerdict));
                RaisePropertyChanged(nameof(LatestPreparedAt));
                RaisePropertyChanged(nameof(LatestRequestedAt));
                RaisePropertyChanged(nameof(LatestAcceptanceAt));
                RaisePropertyChanged(nameof(LatestFeedbackAt));
                RaisePropertyChanged(nameof(LatestAcceptanceLatency));
                RaisePropertyChanged(nameof(LatestFeedbackLatency));
                RaisePropertyChanged(nameof(LatestFeedbackMatch));
            }
        }
    }

    public PointCatalogProfile? SelectedPointCatalogProfile
    {
        get => _selectedPointCatalogProfile;
        set
        {
            if (SetProperty(ref _selectedPointCatalogProfile, value))
            {
                ApplyPointCatalogProfile(value);
                RaisePropertyChanged(nameof(PointCatalogProfileName));
                RefreshNamedSurfaces();
                RaisePointCatalogCommandState();
            }
        }
    }

    public PointCatalogEntry? SelectedPointCatalogEntry
    {
        get => _selectedPointCatalogEntry;
        set
        {
            if (SetProperty(ref _selectedPointCatalogEntry, value))
            {
                RaisePointCatalogCommandState();
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
    private Task SendBinaryCommandAsync() => RunBusyAsync(async () =>
    {
        var index = CommandPointIndex;
        var mode = SelectedCommandMode;
        var operation = SelectedBinaryOperation;
        var preparedAt = DateTime.Now;

        InsertTop(EventLogs, new EventLogEntry
        {
            TimestampLocal = preparedAt,
            EventType = "Command Prepared",
            Source = "UI",
            PointType = "Binary Output",
            Index = index,
            RawValue = operation.ToString(),
            Value = operation.ToString(),
            Status = mode.ToString(),
            SourceReason = SourceReason.CommandResponse,
            Detail = $"Operator issued binary control request for index {index}: {mode} / {operation}"
        });

        await _service.ExecuteBinaryControlAsync(index, mode, operation, preparedAt);
    });

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
        existing.DisplayName = row.DisplayName;
        existing.ScadaTag = row.ScadaTag;
        existing.RawValue = row.RawValue;

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
        SendBinaryCommand.RaiseCanExecuteChanged();
    }

    private void RaisePointCatalogCommandState()
    {
        AddPointCatalogEntryCommand?.RaiseCanExecuteChanged();
        RemovePointCatalogEntryCommand?.RaiseCanExecuteChanged();
        SavePointCatalogProfileCommand?.RaiseCanExecuteChanged();
        ReloadPointCatalogProfileCommand?.RaiseCanExecuteChanged();
    }

    private void ReplaceLifecycle(IReadOnlyList<CommandLifecycleEntry> lifecycle)
    {
        CommandLifecycle.Clear();
        foreach (var entry in lifecycle.OrderByDescending(x => x.TimestampLocal))
        {
            CommandLifecycle.Add(entry);
        }
    }

    private void RaiseConnectionSummaryChanged()
    {
        RaisePropertyChanged(nameof(ConnectionProfile));
        RaisePropertyChanged(nameof(PointCatalogProfileName));
        RaisePropertyChanged(nameof(ConnectionTarget));
        RaisePropertyChanged(nameof(PollingProfile));
        RaisePropertyChanged(nameof(SerialProfile));
        RaisePropertyChanged(nameof(IsTcpTransport));
        RaisePropertyChanged(nameof(IsSerialTransport));
    }

    private ObservableCollection<PointCatalogProfile> LoadPointCatalogProfiles()
    {
        var result = new ObservableCollection<PointCatalogProfile>();
        var profileDirectory = Path.Combine(AppContext.BaseDirectory, "MetadataProfiles");
        if (!Directory.Exists(profileDirectory))
        {
            return result;
        }

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (var file in Directory.GetFiles(profileDirectory, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(file);
            var profile = JsonSerializer.Deserialize<PointCatalogProfile>(json, serializerOptions);
            if (profile is not null)
            {
                profile.FilePath = file;
                result.Add(profile);
            }
        }

        return result;
    }

    private void ApplyPointCatalogProfile(PointCatalogProfile? profile)
    {
        PointCatalog.Clear();
        if (profile is not null)
        {
            foreach (var entry in profile.Points)
            {
                PointCatalog.Add(entry);
            }
        }

        RebuildPointCatalogIndex();
        SelectedPointCatalogEntry = PointCatalog.FirstOrDefault();
    }

    private void RebuildPointCatalogIndex()
    {
        _pointCatalog.Clear();
        foreach (var entry in PointCatalog)
        {
            _pointCatalog[BuildCatalogKey(entry.PointType, entry.Index)] = entry;
        }
    }

    private ValueViewerRow Enrich(ValueViewerRow row)
    {
        if (_pointCatalog.TryGetValue(BuildCatalogKey(row.PointType, row.Index), out var point))
        {
            row.DisplayName = point.DisplayName;
            row.ScadaTag = point.ScadaTag;
            row.Value = MapStateText(row.RawValue, point);
        }

        return row;
    }

    private SoeEventRow Enrich(SoeEventRow row)
    {
        if (_pointCatalog.TryGetValue(BuildCatalogKey(row.PointType, row.Index), out var point))
        {
            row.DisplayName = point.DisplayName;
            row.ScadaTag = point.ScadaTag;
            row.Value = MapStateText(string.IsNullOrWhiteSpace(row.RawValue) ? row.Value : row.RawValue, point);
            row.PreviousValue = MapStateText(string.IsNullOrWhiteSpace(row.RawPreviousValue) ? row.PreviousValue : row.RawPreviousValue, point);
        }

        return row;
    }

    private EventLogEntry Enrich(EventLogEntry row)
    {
        if (row.Index.HasValue && _pointCatalog.TryGetValue(BuildCatalogKey(row.PointType, row.Index.Value), out var point))
        {
            row.DisplayName = point.DisplayName;
            row.ScadaTag = point.ScadaTag;
            row.Value = MapStateText(string.IsNullOrWhiteSpace(row.RawValue) ? row.Value : row.RawValue, point);
            row.PreviousValue = MapStateText(string.IsNullOrWhiteSpace(row.RawPreviousValue) ? row.PreviousValue : row.RawPreviousValue, point);
        }

        return row;
    }

    private CommandTransaction Enrich(CommandTransaction transaction)
    {
        if (_pointCatalog.TryGetValue(BuildCatalogKey(transaction.PointType, transaction.PointIndex), out var point))
        {
            return transaction with
            {
                DisplayName = point.DisplayName,
                ScadaTag = point.ScadaTag
            };
        }

        return transaction;
    }

    private static string BuildCatalogKey(string pointType, ushort index) => $"{pointType}:{index}";

    private static string MapStateText(string value, PointCatalogEntry point)
    {
        if (string.Equals(value, bool.FalseString, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(point.StateTextOff))
        {
            return point.StateTextOff;
        }

        if (string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(point.StateTextOn))
        {
            return point.StateTextOn;
        }

        return value;
    }

    private void RefreshNamedSurfaces()
    {
        for (var i = 0; i < ValueViewer.Count; i++)
        {
            ValueViewer[i] = Enrich(ValueViewer[i]);
        }

        for (var i = 0; i < EventLogs.Count; i++)
        {
            EventLogs[i] = Enrich(EventLogs[i]);
        }

        for (var i = 0; i < SoeAudit.Count; i++)
        {
            SoeAudit[i] = Enrich(SoeAudit[i]);
        }

        if (LatestCommandTransaction is not null)
        {
            LatestCommandTransaction = Enrich(LatestCommandTransaction);
        }
    }

    private void AddPointCatalogEntry()
    {
        var nextIndex = PointCatalog.Select(x => (int)x.Index).DefaultIfEmpty(-1).Max() + 1;
        var entry = new PointCatalogEntry
        {
            Index = (ushort)Math.Max(0, nextIndex),
            PointType = "Binary Input",
            ObjectVariation = "2/2",
            DisplayName = "New Signal"
        };
        PointCatalog.Add(entry);
        SelectedPointCatalogEntry = entry;
        RebuildPointCatalogIndex();
        RefreshNamedSurfaces();
    }

    private void RemovePointCatalogEntry()
    {
        if (SelectedPointCatalogEntry is null)
        {
            return;
        }

        PointCatalog.Remove(SelectedPointCatalogEntry);
        SelectedPointCatalogEntry = PointCatalog.FirstOrDefault();
        RebuildPointCatalogIndex();
        RefreshNamedSurfaces();
    }

    private void SavePointCatalogProfile()
    {
        if (SelectedPointCatalogProfile is null || string.IsNullOrWhiteSpace(SelectedPointCatalogProfile.FilePath))
        {
            return;
        }

        SelectedPointCatalogProfile.Points = PointCatalog.ToList();
        var json = JsonSerializer.Serialize(SelectedPointCatalogProfile, _profileSerializerOptions);
        File.WriteAllText(SelectedPointCatalogProfile.FilePath, json);
        RebuildPointCatalogIndex();
        RefreshNamedSurfaces();
        ConnectionDetail = $"Saved point database profile: {SelectedPointCatalogProfile.Name}";
    }

    private void ReloadPointCatalogProfile()
    {
        if (SelectedPointCatalogProfile is null || string.IsNullOrWhiteSpace(SelectedPointCatalogProfile.FilePath) || !File.Exists(SelectedPointCatalogProfile.FilePath))
        {
            return;
        }

        var json = File.ReadAllText(SelectedPointCatalogProfile.FilePath);
        var reloaded = JsonSerializer.Deserialize<PointCatalogProfile>(json, _profileSerializerOptions);
        if (reloaded is null)
        {
            return;
        }

        reloaded.FilePath = SelectedPointCatalogProfile.FilePath;
        var profileIndex = PointCatalogProfiles.IndexOf(SelectedPointCatalogProfile);
        PointCatalogProfiles[profileIndex] = reloaded;
        SelectedPointCatalogProfile = reloaded;
        ConnectionDetail = $"Reloaded point database profile: {SelectedPointCatalogProfile.Name}";
    }
}
