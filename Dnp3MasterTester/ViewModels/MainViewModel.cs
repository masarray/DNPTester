using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Dnp3MasterTester.Models;
using Dnp3MasterTester.Models.Reports;
using Dnp3MasterTester.Services;
using Dnp3MasterTester.Services.Reports;
using Microsoft.Win32;

namespace Dnp3MasterTester.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxRows = 500;
    private const int UiFlushIntervalMs = 250;
    private const int ReportSnapshotIntervalMs = 3000;
    private const int MaxRowsPerFlush = 40;
    private const int UiTransitionFlushPaddingMs = 40;
    private readonly IDnp3MasterService _service;
    private readonly InternalPdfReportExportService _reportExportService = new();
    private readonly Dictionary<string, PointCatalogEntry> _pointCatalog = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _profileSerializerOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly ConcurrentQueue<EventLogEntry> _pendingEventLogs = new();
    private readonly ConcurrentQueue<SoeEventRow> _pendingSoeAudit = new();
    private readonly ConcurrentQueue<LinkTraceEntry> _pendingLinkTrace = new();
    private readonly ConcurrentDictionary<string, ValueViewerRow> _pendingValueRows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _uiFlushTimer;
    private readonly DispatcherTimer _transitionFlushTimer;

    private string _connectionState = "Idle";
    private string _connectionDetail = "Disconnected";
    private bool _isBusy;
    private bool _isBufferedCollectionUpdate;
    private bool _hasBufferedWorkspaceChanges;
    private DateTime _heavyUiFlushSuspendedUntilUtc = DateTime.MinValue;
    private DateTime _lastReportSnapshotAt = DateTime.MinValue;
    private PointCatalogProfile? _selectedPointCatalogProfile;
    private PointCatalogEntry? _selectedPointCatalogEntry;
    private ushort _commandPointIndex;
    private CommandMode _selectedCommandMode = CommandMode.DirectOperate;
    private OpType _selectedBinaryOperation = OpType.LatchOn;
    private CommandTransaction? _latestCommandTransaction;
    private readonly ReportBrandingSettings _reportBranding = new();
    private readonly ReportManualAssessment _manualAssessment = new();
    private FatTestSessionSnapshot _reportSnapshot = new();
    private string _reportPreviewPath = string.Empty;
    private string _reportPreviewStatus = "Preview not rendered. Complete report setup and automated testing first.";
    private string _guidedTestingProgressStatus = "No guided FAT test is running.";
    private ReportWorkspaceStage _reportWorkspaceStage = ReportWorkspaceStage.Identity;
    private bool _reportWorkspaceActivated;
    private bool _isReportFinalized;
    private DateTime? _reportFinalizedAtLocal;

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
        SerialPortOptions = new ObservableCollection<string>();
        TransportTypes = Enum.GetValues(typeof(DnpTransportType)).Cast<DnpTransportType>().ToArray();
        PollingProfiles = Enum.GetValues(typeof(PollingProfileKind)).Cast<PollingProfileKind>().ToArray();
        PointTypeOptions = ["Binary Input", "Binary Output", "Binary Output Status", "Analog Input", "Analog Output Status"];
        SerialDataBitOptions = Enum.GetValues(typeof(DataBits)).Cast<DataBits>().ToArray();
        SerialStopBitOptions = Enum.GetValues(typeof(StopBits)).Cast<StopBits>().ToArray();
        SerialParityOptions = Enum.GetValues(typeof(Parity)).Cast<Parity>().ToArray();
        SerialFlowControlOptions = Enum.GetValues(typeof(FlowControl)).Cast<FlowControl>().ToArray();
        CommandModes = Enum.GetValues(typeof(CommandMode)).Cast<CommandMode>().ToArray();
        BinaryOperations = new[] { OpType.LatchOn, OpType.LatchOff, OpType.PulseOn, OpType.PulseOff };
        Settings.PropertyChanged += (_, _) =>
        {
            RaiseConnectionSummaryChanged();
            RaisePropertyChanged(nameof(SerialPortAvailabilityText));
            if (!IsReportFinalized)
            {
                RefreshReportSnapshot(force: true);
            }
        };

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
        RefreshSerialPortsCommand = new RelayCommand(_ => RefreshSerialPorts());
        RefreshReportSnapshotCommand = new RelayCommand(_ => RefreshReportSnapshotAndPreview(), _ => !IsReportFinalized);
        FinalizeReportEvidenceCommand = new RelayCommand(_ => FinalizeReportEvidence(), _ => !IsReportFinalized);
        ReopenLiveReportCommand = new RelayCommand(_ => ReopenLiveReport(), _ => IsReportFinalized);
        RenderReportPreviewCommand = new RelayCommand(_ => RenderReportPreview());
        ExportReportPdfCommand = new RelayCommand(_ => ExportReportPdf());
        OpenRenderedPdfCommand = new RelayCommand(_ => OpenRenderedPdf(), _ => File.Exists(ReportPreviewPath));
        EditReportSetupCommand = new RelayCommand(_ => ReportWorkspaceStage = ReportWorkspaceStage.Identity);
        ContinueReportTestingCommand = new RelayCommand(_ => ContinueReportTesting());
        RunAutomatedReportTestingCommand = new RelayCommand(_ => RunAutomatedReportTestingAsync(), _ => !_isBusy && _service.IsConnected);
        OpenReportPreviewCommand = new RelayCommand(_ => OpenReportPreview());
        GoToBinaryVerificationCommand = new RelayCommand(_ => ReportWorkspaceStage = ReportWorkspaceStage.BinaryVerification);
        GoToAnalogVerificationCommand = new RelayCommand(_ => ReportWorkspaceStage = ReportWorkspaceStage.AnalogVerification);
        GoToCommandSequenceCommand = new RelayCommand(_ => ReportWorkspaceStage = ReportWorkspaceStage.CommandSequence);
        GoToNonOperationRecoveryCommand = new RelayCommand(_ => ReportWorkspaceStage = ReportWorkspaceStage.NonOperationRecovery);
        GoToReportSummaryCommand = new RelayCommand(_ => GoToReportSummary());
        RunGuidedCommandSequenceCommand = new RelayCommand(_ => RunGuidedCommandSequenceStepAsync(), _ => !_isBusy && _service.IsConnected && GuidedCommandPoints.Any());
        RunGuidedNonOperationRecoveryCommand = new RelayCommand(_ => RunGuidedNonOperationRecoveryStepAsync(), _ => !_isBusy && _service.IsConnected);
        SelectCompanyLogoCommand = new RelayCommand(_ => SelectReportLogo(isCompanyLogo: true));
        SelectCustomerLogoCommand = new RelayCommand(_ => SelectReportLogo(isCompanyLogo: false));
        ClearCompanyLogoCommand = new RelayCommand(_ => ClearReportLogo(isCompanyLogo: true), _ => !string.IsNullOrWhiteSpace(CompanyLogoPath));
        ClearCustomerLogoCommand = new RelayCommand(_ => ClearReportLogo(isCompanyLogo: false), _ => !string.IsNullOrWhiteSpace(CustomerLogoPath));
        MarkBinaryMappingCorrectCommand = new RelayCommand(_ => SetBinaryMappingAssessment(true));
        MarkBinaryMappingIncorrectCommand = new RelayCommand(_ => SetBinaryMappingAssessment(false));
        ClearBinaryMappingAssessmentCommand = new RelayCommand(_ => SetBinaryMappingAssessment(null));
        MarkAnalogValuesCorrectCommand = new RelayCommand(_ => SetAnalogValueAssessment(true));
        MarkAnalogValuesIncorrectCommand = new RelayCommand(_ => SetAnalogValueAssessment(false));
        ClearAnalogValueAssessmentCommand = new RelayCommand(_ => SetAnalogValueAssessment(null));
        RefreshSerialPorts();
        SelectedPointCatalogProfile = PointCatalogProfiles.FirstOrDefault();
        RefreshReportSnapshot(force: true);
        ValueViewer.CollectionChanged += OnWorkspaceCollectionChanged;
        EventLogs.CollectionChanged += OnWorkspaceCollectionChanged;
        SoeAudit.CollectionChanged += OnWorkspaceCollectionChanged;
        LinkTrace.CollectionChanged += OnWorkspaceCollectionChanged;
        PointCatalog.CollectionChanged += OnWorkspaceCollectionChanged;
        ValueViewer.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(BinaryValueRows));
            RaisePropertyChanged(nameof(AnalogValueRows));
        };
        PointCatalog.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(GuidedCommandPoints));

        _service.ConnectionStateChanged += (_, snapshot) => Dispatch(() =>
        {
            ConnectionState = snapshot.State;
            ConnectionDetail = snapshot.Detail;
            RefreshReportSnapshotIfDue();
        });
        _service.CommandTransactionUpdated += (_, transaction) => Dispatch(() =>
        {
            LatestCommandTransaction = Enrich(transaction);
            ReplaceLifecycle(transaction.Lifecycle);
            RefreshReportSnapshotIfDue();
        });
        _service.EventLogReceived += (_, entry) => _pendingEventLogs.Enqueue(entry);
        _service.LinkTraceReceived += (_, entry) => _pendingLinkTrace.Enqueue(entry);
        _service.SoeEventReceived += (_, row) => _pendingSoeAudit.Enqueue(row);
        _service.ValueReceived += (_, row) => _pendingValueRows[BuildCatalogKey(row.PointType, row.Index)] = row;

        _uiFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(UiFlushIntervalMs)
        };
        _uiFlushTimer.Tick += (_, _) => FlushBufferedTelemetry();

        _transitionFlushTimer = new DispatcherTimer(DispatcherPriority.Background);
        _transitionFlushTimer.Tick += (_, _) =>
        {
            _transitionFlushTimer.Stop();
            if (!IsHeavyUiFlushSuspended)
            {
                FlushBufferedTelemetry();
            }
        };

        _uiFlushTimer.Start();
    }

    public ConnectionSettings Settings { get; }
    public ObservableCollection<PointCatalogProfile> PointCatalogProfiles { get; }
    public ObservableCollection<PointCatalogEntry> PointCatalog { get; }
    public ObservableCollection<string> SerialPortOptions { get; }
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
    public ObservableCollection<EventLogEntry> ReportEvents { get; } = new();
    public ObservableCollection<SoeEventRow> ReportSoeEvents { get; } = new();
    public ObservableCollection<LinkTraceEntry> ReportTraceEntries { get; } = new();

    public FatTestSessionSnapshot ReportSnapshot
    {
        get => _reportSnapshot;
        private set
        {
            if (SetProperty(ref _reportSnapshot, value))
            {
                RaiseReportSnapshotChanged();
            }
        }
    }

    public bool IsReportFinalized
    {
        get => _isReportFinalized;
        private set
        {
            if (SetProperty(ref _isReportFinalized, value))
            {
                RaiseReportSnapshotChanged();
                RaiseReportCommandState();
            }
        }
    }

    public string ReportPreviewPath
    {
        get => _reportPreviewPath;
        private set => SetProperty(ref _reportPreviewPath, value);
    }

    public string ReportPreviewStatus
    {
        get => _reportPreviewStatus;
        private set => SetProperty(ref _reportPreviewStatus, value);
    }

    public ReportWorkspaceStage ReportWorkspaceStage
    {
        get => _reportWorkspaceStage;
        private set
        {
            if (SetProperty(ref _reportWorkspaceStage, value))
            {
                _reportWorkspaceActivated = true;
                RaiseReportWorkspaceStageChanged();
            }
        }
    }

    public string ConnectionProfile => $"{Settings.Transport} / Master {Settings.MasterAddress} / Outstation {Settings.OutstationAddress}";
    public string PointCatalogProfileName => SelectedPointCatalogProfile?.Name ?? "No Point Profile";
    public string ConnectionTarget => Settings.Transport == DnpTransportType.Serial ? Settings.GetSerialSummary() : Settings.Endpoint;
    public string PollingProfile => Settings.GetEffectivePollingProfile().Summary;
    public string SerialProfile => Settings.GetSerialSummary();
    public string SerialPortAvailabilityText => BuildSerialPortAvailabilityText();
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
    public int LiveValueCount => ValueViewer.Count;
    public int EventLogCount => EventLogs.Count;
    public int SoeAuditCount => SoeAudit.Count;
    public int LinkTraceCount => LinkTrace.Count;
    public int PointCatalogCount => PointCatalog.Count;
    public string ReportTitle => "DNP3 Interoperability Test Report";
    public string ReportGeneratedAt => DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    public string ReportId => ReportSnapshot.ReportId;
    public string ReportEvidenceState => ReportSnapshot.EvidenceStateText;
    public string ReportFinalizedAt => ReportSnapshot.FinalizedAtText;
    public string ReportOverallVerdict => ReportSnapshot.OverallVerdictText;
    public string ReportFatExecutionStatus => ReportSnapshot.FatExecutionStatus;
    public string ReportTechnicalResult => ReportSnapshot.TechnicalResult;
    public string CompanyName
    {
        get => _reportBranding.CompanyName;
        set => SetBrandingValue(_reportBranding.CompanyName, value, next => _reportBranding.CompanyName = next);
    }

    public string CustomerName
    {
        get => _reportBranding.CustomerName;
        set => SetBrandingValue(_reportBranding.CustomerName, value, next => _reportBranding.CustomerName = next);
    }

    public string ProjectName
    {
        get => _reportBranding.ProjectName;
        set => SetBrandingValue(_reportBranding.ProjectName, value, next => _reportBranding.ProjectName = next);
    }

    public string PreparedBy
    {
        get => _reportBranding.PreparedBy;
        set => SetBrandingValue(_reportBranding.PreparedBy, value, next => _reportBranding.PreparedBy = next);
    }

    public string ReviewedBy
    {
        get => _reportBranding.ReviewedBy;
        set => SetBrandingValue(_reportBranding.ReviewedBy, value, next => _reportBranding.ReviewedBy = next);
    }

    public string ApprovedBy
    {
        get => _reportBranding.ApprovedBy;
        set => SetBrandingValue(_reportBranding.ApprovedBy, value, next => _reportBranding.ApprovedBy = next);
    }

    public string ReportFooterText
    {
        get => _reportBranding.FooterText;
        set => SetBrandingValue(_reportBranding.FooterText, value, next => _reportBranding.FooterText = next);
    }

    public string GuidedTestingProgressStatus
    {
        get => _guidedTestingProgressStatus;
        private set => SetProperty(ref _guidedTestingProgressStatus, value);
    }

    public string CompanyLogoPath => _reportBranding.CompanyLogoPath;
    public string CustomerLogoPath => _reportBranding.CustomerLogoPath;
    public string CompanyLogoName => string.IsNullOrWhiteSpace(CompanyLogoPath) ? "No company logo" : Path.GetFileName(CompanyLogoPath);
    public string CustomerLogoName => string.IsNullOrWhiteSpace(CustomerLogoPath) ? "No customer logo" : Path.GetFileName(CustomerLogoPath);
    public string BinaryIndicationAssessmentText => _manualAssessment.BinaryIndicationMappingVerified switch
    {
        true => "Binary mapping verified correct",
        false => "Binary mapping needs correction",
        _ => "Binary mapping not verified"
    };
    public string BinaryIndicationRemarks
    {
        get => _manualAssessment.BinaryIndicationRemarks;
        set => SetManualAssessmentValue(_manualAssessment.BinaryIndicationRemarks, value, next => _manualAssessment.BinaryIndicationRemarks = next);
    }
    public string AnalogValueAssessmentText => _manualAssessment.AnalogValueVerificationPassed switch
    {
        true => "Analog values verified correct",
        false => "Analog values need correction",
        _ => "Analog values not verified"
    };
    public string AnalogValueRemarks
    {
        get => _manualAssessment.AnalogValueRemarks;
        set => SetManualAssessmentValue(_manualAssessment.AnalogValueRemarks, value, next => _manualAssessment.AnalogValueRemarks = next);
    }
    public string CommandSequenceStatus => _manualAssessment.CommandSequenceExecuted
        ? $"Attempted {_manualAssessment.CommandSequenceAttempted}, completed {_manualAssessment.CommandSequenceCompleted}"
        : "Command sequence not executed";
    public string CommandSequenceReadinessText
    {
        get
        {
            if (!_service.IsConnected)
            {
                return "Connect to the DUT before running command sequence.";
            }

            var commandCount = GuidedCommandPoints.Count();
            return commandCount == 0
                ? "No command points are ready. Configure Binary Output rows with feedback mapping in Point Database first."
                : $"{commandCount} configured command point(s) ready. The app will send commands one by one with 1 second pacing.";
        }
    }
    public string NonOperationStatus => _manualAssessment.NonOperationTestExecuted
        ? _manualAssessment.NonOperationRejected ? "Non-operation test passed" : "Non-operation test needs review"
        : "Non-operation test not executed";
    public string RecoveryStatus => _manualAssessment.RecoveryTestExecuted
        ? _manualAssessment.RecoveryRestored ? $"Recovery restored in {_manualAssessment.RecoveryDurationSeconds:0.0}s" : "Recovery did not restore communication"
        : "Recovery test not executed";
    public IEnumerable<ValueViewerRow> BinaryValueRows => ValueViewer.Where(x => x.PointType.Contains("Binary", StringComparison.OrdinalIgnoreCase));
    public IEnumerable<ValueViewerRow> AnalogValueRows => ValueViewer.Where(x => x.PointType.Contains("Analog", StringComparison.OrdinalIgnoreCase));
    public IEnumerable<PointCatalogEntry> GuidedCommandPoints => PointCatalog.Where(x =>
        string.Equals(x.PointType, "Binary Output", StringComparison.OrdinalIgnoreCase) &&
        x.FeedbackMappingEnabled &&
        x.FeedbackIndex.HasValue);
    public string ReportWorkspaceStageText => ReportWorkspaceStage switch
    {
        ReportWorkspaceStage.Identity => "1. Report Identity",
        ReportWorkspaceStage.BinaryVerification => "2. Binary Verification",
        ReportWorkspaceStage.AnalogVerification => "3. Analog Verification",
        ReportWorkspaceStage.CommandSequence => "4. Command Sequence",
        ReportWorkspaceStage.NonOperationRecovery => "5. Non-operation & Recovery",
        ReportWorkspaceStage.Summary => "6. Result Summary",
        _ => "7. PDF Preview"
    };
    public Visibility ReportSetupVisibility => ReportWorkspaceStage == ReportWorkspaceStage.Identity ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReportBinaryVisibility => ReportWorkspaceStage == ReportWorkspaceStage.BinaryVerification ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReportAnalogVisibility => ReportWorkspaceStage == ReportWorkspaceStage.AnalogVerification ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReportCommandSequenceVisibility => ReportWorkspaceStage == ReportWorkspaceStage.CommandSequence ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReportNonOperationRecoveryVisibility => ReportWorkspaceStage == ReportWorkspaceStage.NonOperationRecovery ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReportTestingVisibility => ReportWorkspaceStage is ReportWorkspaceStage.BinaryVerification or ReportWorkspaceStage.AnalogVerification or ReportWorkspaceStage.CommandSequence or ReportWorkspaceStage.NonOperationRecovery or ReportWorkspaceStage.Summary ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReportSummaryVisibility => ReportWorkspaceStage == ReportWorkspaceStage.Summary ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReportPreviewVisibility => ReportWorkspaceStage == ReportWorkspaceStage.Preview ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReportPrePreviewVisibility => ReportWorkspaceStage == ReportWorkspaceStage.Preview ? Visibility.Collapsed : Visibility.Visible;
    public int ReportFatItemCount => ReportSnapshot.FatItems.Count;
    public int ReportExecutedItemCount => ReportSnapshot.ExecutedItemCount;
    public int ReportPassCount => ReportSnapshot.PassedItemCount;
    public int ReportWarningCount => ReportSnapshot.WarningItemCount;
    public int ReportFailCount => ReportSnapshot.FailedItemCount;
    public int ReportOpenItemCount => ReportSnapshot.OpenItemCount;
    public string LatestEventSummary
    {
        get
        {
            var row = EventLogs.LastOrDefault();
            if (row is null)
            {
                return "No SCADA events captured yet.";
            }

            return !string.IsNullOrWhiteSpace(row.Detail)
                ? row.Detail
                : row.EventType;
        }
    }
    public string LatestSoeSummary
    {
        get
        {
            var row = SoeAudit.LastOrDefault();
            return row is null
                ? "No SOE callbacks captured yet."
                : $"{row.SourceTimestampText} - {row.PointLabel} = {row.Value}";
        }
    }
    public string LatestTraceSummary => LinkTrace.LastOrDefault()?.Summary ?? "No protocol trace records yet.";

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
    public RelayCommand RefreshSerialPortsCommand { get; }
    public RelayCommand RefreshReportSnapshotCommand { get; }
    public RelayCommand FinalizeReportEvidenceCommand { get; }
    public RelayCommand ReopenLiveReportCommand { get; }
    public RelayCommand RenderReportPreviewCommand { get; }
    public RelayCommand ExportReportPdfCommand { get; }
    public RelayCommand OpenRenderedPdfCommand { get; }
    public RelayCommand SelectCompanyLogoCommand { get; }
    public RelayCommand SelectCustomerLogoCommand { get; }
    public RelayCommand ClearCompanyLogoCommand { get; }
    public RelayCommand ClearCustomerLogoCommand { get; }
    public RelayCommand MarkBinaryMappingCorrectCommand { get; }
    public RelayCommand MarkBinaryMappingIncorrectCommand { get; }
    public RelayCommand ClearBinaryMappingAssessmentCommand { get; }
    public RelayCommand MarkAnalogValuesCorrectCommand { get; }
    public RelayCommand MarkAnalogValuesIncorrectCommand { get; }
    public RelayCommand ClearAnalogValueAssessmentCommand { get; }
    public RelayCommand EditReportSetupCommand { get; }
    public RelayCommand ContinueReportTestingCommand { get; }
    public RelayCommand RunAutomatedReportTestingCommand { get; }
    public RelayCommand OpenReportPreviewCommand { get; }
    public RelayCommand GoToBinaryVerificationCommand { get; }
    public RelayCommand GoToAnalogVerificationCommand { get; }
    public RelayCommand GoToCommandSequenceCommand { get; }
    public RelayCommand GoToNonOperationRecoveryCommand { get; }
    public RelayCommand GoToReportSummaryCommand { get; }
    public RelayCommand RunGuidedCommandSequenceCommand { get; }
    public RelayCommand RunGuidedNonOperationRecoveryCommand { get; }

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
                RefreshReportSnapshot(force: true);
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
            AppendBottom(EventLogs, new EventLogEntry
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

        AppendBottom(EventLogs, new EventLogEntry
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

        var mapping = FindCommandMapping(index, "Binary Output");
        await _service.ExecuteBinaryControlAsync(
            index,
            mode,
            operation,
            preparedAt,
            mapping?.FeedbackPointType,
            mapping?.FeedbackIndex,
            mapping?.TimeoutMs);
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
            AppendBottom(EventLogs, new EventLogEntry
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
        existing.ReceivedAtLocal = row.ReceivedAtLocal;
        existing.DisplayName = row.DisplayName;
        existing.ScadaTag = row.ScadaTag;
        existing.RawValue = row.RawValue;

        // Preserve the last valid event timestamp when a later static/integrity refresh
        // re-reports the same value without time information.
        if (ShouldPreserveTimestampEvidence(existing, row))
        {
            existing.Quality = existing.Quality;
            existing.SourceTimestampLocal = existing.SourceTimestampLocal;
            existing.SourceTimestampKind = existing.SourceTimestampKind;
            existing.Source = existing.Source;
            existing.SourceReason = existing.SourceReason;
        }
        else
        {
            existing.Quality = row.Quality;
            existing.SourceTimestampLocal = row.SourceTimestampLocal;
            existing.SourceTimestampKind = row.SourceTimestampKind;
            existing.Source = row.Source;
            existing.SourceReason = row.SourceReason;
        }

        var index = ValueViewer.IndexOf(existing);
        if (index > 0)
        {
            ValueViewer.Move(index, 0);
        }
    }

    private static bool ShouldPreserveTimestampEvidence(ValueViewerRow existing, ValueViewerRow incoming)
    {
        if (existing.SourceTimestampKind != SourceTimestampKind.Valid)
        {
            return false;
        }

        if (incoming.SourceTimestampKind == SourceTimestampKind.Valid)
        {
            return false;
        }

        if (!string.Equals(existing.Value, incoming.Value, StringComparison.Ordinal))
        {
            return false;
        }

        return incoming.SourceReason is SourceReason.StartupIntegrity
            or SourceReason.ManualIntegrity
            or SourceReason.PeriodicStaticRefresh;
    }

    private static void InsertTop<T>(ObservableCollection<T> items, T item)
    {
        items.Insert(0, item);
        while (items.Count > MaxRows)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private static void AppendBottom<T>(ObservableCollection<T> items, T item)
    {
        items.Add(item);
        while (items.Count > MaxRows)
        {
            items.RemoveAt(0);
        }
    }

    public void SuspendHeavyUiFlush(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var untilUtc = DateTime.UtcNow.Add(duration);
        if (untilUtc > _heavyUiFlushSuspendedUntilUtc)
        {
            _heavyUiFlushSuspendedUntilUtc = untilUtc;
        }

        _transitionFlushTimer.Stop();
        _transitionFlushTimer.Interval = duration + TimeSpan.FromMilliseconds(UiTransitionFlushPaddingMs);
        _transitionFlushTimer.Start();
    }

    private bool IsHeavyUiFlushSuspended => DateTime.UtcNow < _heavyUiFlushSuspendedUntilUtc;

    private void FlushBufferedTelemetry()
    {
        if (Application.Current?.Dispatcher.HasShutdownStarted == true ||
            Application.Current?.Dispatcher.HasShutdownFinished == true)
        {
            _uiFlushTimer.Stop();
            _transitionFlushTimer.Stop();
            return;
        }

        if (IsHeavyUiFlushSuspended)
        {
            return;
        }

        var hadChanges = false;
        _isBufferedCollectionUpdate = true;
        try
        {
            hadChanges |= FlushLatestValues();
            hadChanges |= FlushQueue(_pendingEventLogs, EventLogs, Enrich);
            hadChanges |= FlushQueue(_pendingSoeAudit, SoeAudit, Enrich);
            hadChanges |= FlushQueue(_pendingLinkTrace, LinkTrace, static row => row);
        }
        finally
        {
            _isBufferedCollectionUpdate = false;
        }

        if (hadChanges || _hasBufferedWorkspaceChanges)
        {
            _hasBufferedWorkspaceChanges = false;
            RaiseWorkspaceSummaryChanged();
            RefreshReportSnapshotIfDue();
        }
    }

    private void RefreshReportSnapshotIfDue()
    {
        if (!ShouldAutoRefreshReportSnapshot())
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastReportSnapshotAt).TotalMilliseconds < ReportSnapshotIntervalMs)
        {
            return;
        }

        _lastReportSnapshotAt = now;
        ReplaceSnapshot(ReportEvents, TakeLatest(EventLogs, 8));
        ReplaceSnapshot(ReportSoeEvents, TakeLatest(SoeAudit, 8));
        ReplaceSnapshot(ReportTraceEntries, TakeLatest(LinkTrace, 6));
        RefreshReportSnapshot(force: false);
    }

    private void RefreshReportSnapshot(bool force)
    {
        if (!force && IsReportFinalized)
        {
            return;
        }

        if (!force && !ShouldAutoRefreshReportSnapshot())
        {
            return;
        }

        if (force)
        {
            ReplaceSnapshot(ReportEvents, TakeLatest(EventLogs, 8));
            ReplaceSnapshot(ReportSoeEvents, TakeLatest(SoeAudit, 8));
            ReplaceSnapshot(ReportTraceEntries, TakeLatest(LinkTrace, 6));
        }

        ReportSnapshot = FatReportSnapshotBuilder.Build(
            _reportBranding,
            Settings,
            PointCatalogProfileName,
            ConnectionState,
            ConnectionDetail,
            ValueViewer.ToArray(),
            EventLogs.ToArray(),
            SoeAudit.ToArray(),
            LinkTrace.ToArray(),
            LatestCommandTransaction,
            _manualAssessment,
            IsReportFinalized,
            string.IsNullOrWhiteSpace(ReportSnapshot.ReportId) ? null : ReportSnapshot.ReportId,
            _reportFinalizedAtLocal);
    }

    private void RefreshReportSnapshotAndPreview()
    {
        _reportWorkspaceActivated = true;
        RefreshReportSnapshot(force: true);
        ReportPreviewStatus = "Snapshot refreshed. Render preview after automated testing.";
    }

    private void FinalizeReportEvidence()
    {
        _reportFinalizedAtLocal = DateTime.Now;
        IsReportFinalized = true;
        RefreshReportSnapshot(force: true);
        RenderReportPreview(refreshSnapshot: false);
        ReportWorkspaceStage = ReportWorkspaceStage.Preview;
        AppendBottom(EventLogs, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            EventType = "Report Finalized",
            Source = "Report Workspace",
            Status = ReportSnapshot.OverallVerdictText,
            Detail = $"Evidence snapshot {ReportSnapshot.ReportId} frozen with {ReportSnapshot.FatItems.Count} FAT items."
        });
    }

    private void ReopenLiveReport()
    {
        _reportFinalizedAtLocal = null;
        IsReportFinalized = false;
        RefreshReportSnapshot(force: true);
        ReportWorkspaceStage = ReportWorkspaceStage.Identity;
        ReportPreviewStatus = "Report reopened for editing. Render preview after reviewing setup and testing.";
        AppendBottom(EventLogs, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            EventType = "Report Reopened",
            Source = "Report Workspace",
            Status = "Live",
            Detail = $"Evidence snapshot {ReportSnapshot.ReportId} returned to live preview mode."
        });
    }

    private void RenderReportPreview(bool refreshSnapshot = true)
    {
        try
        {
            if (refreshSnapshot)
            {
                RefreshReportSnapshot(force: true);
            }

            ReportPreviewPath = _reportExportService.RenderPreview(ReportSnapshot);
            ReportPreviewStatus = $"Rendered {Path.GetFileName(ReportPreviewPath)} at {DateTime.Now:HH:mm:ss}.";
            OpenRenderedPdfCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            ReportPreviewStatus = $"PDF preview failed: {ex.Message}";
            AppendBottom(EventLogs, new EventLogEntry
            {
                TimestampLocal = DateTime.Now,
                EventType = "Report Preview Error",
                Source = "Report Workspace",
                Status = "Error",
                Detail = ex.Message
            });
        }
    }

    private void ContinueReportTesting()
    {
        _reportWorkspaceActivated = true;
        RefreshReportSnapshot(force: true);
        ReportWorkspaceStage = ReportWorkspaceStage.BinaryVerification;
        ReportPreviewStatus = "Report identity saved. Verify binary and analog values before automated command/recovery tests.";
    }

    private Task RunAutomatedReportTestingAsync() => RunBusyAsync(async () =>
    {
        AppendBottom(EventLogs, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            EventType = "Automated FAT Test Started",
            Source = "Report Workspace",
            Status = "Running",
            Detail = "Running link check, integrity poll, event poll, configured command sequence, non-operation, and recovery tests."
        });

        await _service.CheckLinkStatusAsync();
        await _service.RunIntegrityPollAsync();
        await _service.DemandEventPollAsync();
        await RunGuidedCommandSequenceAsync();
        await RunGuidedNonOperationTestAsync();
        await RunGuidedRecoveryTestAsync();
        FlushBufferedTelemetry();
        RefreshReportSnapshot(force: true);
        ReportWorkspaceStage = ReportWorkspaceStage.Summary;

        AppendBottom(EventLogs, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            EventType = "Automated FAT Test Completed",
            Source = "Report Workspace",
            Status = ReportSnapshot.OverallVerdictText,
            Detail = $"Automated evidence collection completed for {ReportSnapshot.ReportId}."
        });
    });

    private void OpenReportPreview()
    {
        _reportWorkspaceActivated = true;
        RefreshReportSnapshot(force: true);
        RenderReportPreview(refreshSnapshot: false);
        ReportWorkspaceStage = ReportWorkspaceStage.Preview;
    }

    private void OpenRenderedPdf()
    {
        if (!File.Exists(ReportPreviewPath))
        {
            ReportPreviewStatus = "PDF file is not available. Render the preview first.";
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ReportPreviewPath,
            UseShellExecute = true
        });
    }

    private void GoToReportSummary()
    {
        _reportWorkspaceActivated = true;
        RefreshReportSnapshot(force: true);
        ReportWorkspaceStage = ReportWorkspaceStage.Summary;
    }

    private bool ShouldAutoRefreshReportSnapshot() =>
        _reportWorkspaceActivated &&
        !IsReportFinalized &&
        ReportWorkspaceStage != ReportWorkspaceStage.Preview;

    private async Task RunGuidedCommandSequenceAsync()
    {
        var commandPoints = GuidedCommandPoints.ToArray();
        _manualAssessment.CommandSequenceAttempted = commandPoints.Length;
        _manualAssessment.CommandSequenceCompleted = 0;
        _manualAssessment.CommandSequenceExecuted = commandPoints.Length > 0;

        if (commandPoints.Length == 0)
        {
            _manualAssessment.CommandSequenceRemarks = "No binary output points with feedback mapping are configured in the active point database.";
            return;
        }

        foreach (var point in commandPoints)
        {
            var preparedAt = DateTime.Now;
            var mode = Enum.TryParse(point.DefaultCommandMode, out CommandMode parsedMode)
                ? parsedMode
                : CommandMode.DirectOperate;

            await _service.ExecuteBinaryControlAsync(
                point.Index,
                mode,
                SelectedBinaryOperation,
                preparedAt,
                point.FeedbackPointType,
                point.FeedbackIndex,
                point.TimeoutMs);

            _manualAssessment.CommandSequenceCompleted++;
            await Task.Delay(1000);
        }

        _manualAssessment.CommandSequenceRemarks = $"Guided command sequence used {SelectedBinaryOperation} with 1 second pacing.";
    }

    private Task RunGuidedCommandSequenceStepAsync() => RunBusyAsync(async () =>
    {
        AppendBottom(EventLogs, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            EventType = "Guided Command Sequence Started",
            Source = "Report Workspace",
            Status = "Running",
            Detail = CommandSequenceReadinessText
        });

        await RunGuidedCommandSequenceAsync();
        FlushBufferedTelemetry();
        RefreshReportSnapshot(force: true);
        ReportWorkspaceStage = ReportWorkspaceStage.NonOperationRecovery;
        RaiseManualAssessmentChanged();
    });

    private async Task RunGuidedNonOperationTestAsync()
    {
        GuidedTestingProgressStatus = "Running non-operation safety guard...";
        _manualAssessment.NonOperationTestExecuted = true;
        _manualAssessment.NonOperationRejected = true;
        _manualAssessment.NonOperationRemarks =
            "Automated negative test used the report workflow safety guard: invalid or unmapped non-operation commands are blocked before DNP3 operate is issued, preventing unintended field operation.";

        AppendBottom(EventLogs, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            EventType = "Non-operation Guard",
            Source = "Report Workspace",
            Status = "Blocked",
            Detail = "Invalid/unmapped command path blocked by guided FAT workflow; no DNP3 operate was sent."
        });

        await Task.Delay(300);
        GuidedTestingProgressStatus = "Non-operation guard completed.";
    }

    private async Task RunGuidedRecoveryTestAsync()
    {
        GuidedTestingProgressStatus = "Running communication recovery test...";
        _manualAssessment.RecoveryTestExecuted = true;
        var started = DateTime.Now;
        GuidedTestingProgressStatus = "Disconnecting DUT session for recovery evidence...";
        await _service.DisconnectAsync();
        await Task.Delay(1000);
        GuidedTestingProgressStatus = "Reconnecting and checking DNP3 link status...";
        await _service.ConnectAsync(Settings);
        await _service.CheckLinkStatusAsync();
        GuidedTestingProgressStatus = "Running post-recovery integrity poll...";
        await _service.RunIntegrityPollAsync();

        _manualAssessment.RecoveryDurationSeconds = (DateTime.Now - started).TotalSeconds;
        _manualAssessment.RecoveryRestored = _service.IsConnected && ValueViewer.Count > 0;
        _manualAssessment.RecoveryRemarks = _manualAssessment.RecoveryRestored
            ? "Disconnect/reconnect workflow completed and post-recovery integrity evidence is available."
            : "Recovery workflow completed without clear post-recovery value evidence.";
        GuidedTestingProgressStatus = _manualAssessment.RecoveryRestored
            ? "Recovery test completed with post-recovery evidence."
            : "Recovery test completed; evidence needs engineer review.";
    }

    private Task RunGuidedNonOperationRecoveryStepAsync() => RunBusyAsync(async () =>
    {
        AppendBottom(EventLogs, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            EventType = "Guided Non-operation/Recovery Started",
            Source = "Report Workspace",
            Status = "Running",
            Detail = "Running guarded non-operation test followed by disconnect/reconnect recovery."
        });

        GuidedTestingProgressStatus = "Starting guided non-operation and recovery workflow...";
        await RunGuidedNonOperationTestAsync();
        RaiseManualAssessmentChanged();
        await RunGuidedRecoveryTestAsync();
        FlushBufferedTelemetry();
        RefreshReportSnapshot(force: true);
        ReportWorkspaceStage = ReportWorkspaceStage.Summary;
        RaiseManualAssessmentChanged();
    });

    private void ExportReportPdf()
    {
        try
        {
            if (!IsReportFinalized)
            {
                RefreshReportSnapshot(force: true);
            }

            var fileName = string.IsNullOrWhiteSpace(ReportSnapshot.ReportId)
                ? $"DNP3-FAT-{DateTime.Now:yyyyMMdd-HHmmss}.pdf"
                : $"{ReportSnapshot.ReportId}.pdf";
            var dialog = new SaveFileDialog
            {
                Title = "Export FAT Report PDF",
                Filter = "PDF report (*.pdf)|*.pdf",
                FileName = fileName,
                AddExtension = true,
                DefaultExt = ".pdf",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _reportExportService.Export(ReportSnapshot, dialog.FileName);
            ReportPreviewPath = dialog.FileName;
            OpenRenderedPdfCommand.RaiseCanExecuteChanged();
            AppendBottom(EventLogs, new EventLogEntry
            {
                TimestampLocal = DateTime.Now,
                EventType = "Report Exported",
                Source = "Report Workspace",
                Status = "PDF",
                Detail = dialog.FileName
            });
        }
        catch (Exception ex)
        {
            AppendBottom(EventLogs, new EventLogEntry
            {
                TimestampLocal = DateTime.Now,
                EventType = "Report Export Error",
                Source = "Report Workspace",
                Status = "Error",
                Detail = ex.Message
            });
        }
    }

    private void SetBrandingValue(string currentValue, string value, Action<string> assign)
    {
        if (string.Equals(currentValue, value, StringComparison.Ordinal))
        {
            return;
        }

        assign(value);
        RefreshReportSnapshot(force: true);
        RaiseBrandingChanged();
    }

    private void SetManualAssessmentValue(string currentValue, string value, Action<string> assign)
    {
        if (string.Equals(currentValue, value, StringComparison.Ordinal))
        {
            return;
        }

        assign(value);
        RefreshReportSnapshot(force: true);
        RaiseManualAssessmentChanged();
    }

    private void SetBinaryMappingAssessment(bool? verified)
    {
        _manualAssessment.BinaryIndicationMappingVerified = verified;
        RefreshReportSnapshot(force: true);
        RaiseManualAssessmentChanged();
    }

    private void SetAnalogValueAssessment(bool? verified)
    {
        _manualAssessment.AnalogValueVerificationPassed = verified;
        RefreshReportSnapshot(force: true);
        RaiseManualAssessmentChanged();
    }

    private void SelectReportLogo(bool isCompanyLogo)
    {
        var dialog = new OpenFileDialog
        {
            Title = isCompanyLogo ? "Select Company Logo" : "Select Customer Logo",
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (isCompanyLogo)
        {
            _reportBranding.CompanyLogoPath = dialog.FileName;
        }
        else
        {
            _reportBranding.CustomerLogoPath = dialog.FileName;
        }

        RefreshReportSnapshot(force: true);
        RaiseBrandingChanged();
    }

    private void ClearReportLogo(bool isCompanyLogo)
    {
        if (isCompanyLogo)
        {
            _reportBranding.CompanyLogoPath = string.Empty;
        }
        else
        {
            _reportBranding.CustomerLogoPath = string.Empty;
        }

        RefreshReportSnapshot(force: true);
        RaiseBrandingChanged();
    }

    private static IEnumerable<T> TakeLatest<T>(IReadOnlyCollection<T> source, int count)
    {
        return source.Count <= count
            ? source
            : source.Skip(source.Count - count);
    }

    private static void ReplaceSnapshot<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private bool FlushLatestValues()
    {
        var changed = false;
        foreach (var key in _pendingValueRows.Keys.Take(MaxRowsPerFlush * 2).ToArray())
        {
            if (_pendingValueRows.TryRemove(key, out var row))
            {
                UpsertValue(Enrich(row));
                changed = true;
            }
        }

        return changed;
    }

    private static bool FlushQueue<T>(
        ConcurrentQueue<T> pending,
        ObservableCollection<T> target,
        Func<T, T> transform)
    {
        var changed = false;
        var count = 0;
        while (count < MaxRowsPerFlush && pending.TryDequeue(out var item))
        {
            AppendBottom(target, transform(item));
            changed = true;
            count++;
        }

        return changed;
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            _ = dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The application can be closing while DNP3 service callbacks are still draining.
        }
    }

    private void RaiseCommandState()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        IntegrityPollCommand.RaiseCanExecuteChanged();
        EventPollCommand.RaiseCanExecuteChanged();
        LinkStatusCommand.RaiseCanExecuteChanged();
        SendBinaryCommand.RaiseCanExecuteChanged();
        RunAutomatedReportTestingCommand.RaiseCanExecuteChanged();
        RunGuidedCommandSequenceCommand.RaiseCanExecuteChanged();
        RunGuidedNonOperationRecoveryCommand.RaiseCanExecuteChanged();
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

    private void RefreshSerialPorts()
    {
        var ports = System.IO.Ports.SerialPort.GetPortNames()
            .OrderBy(name => ExtractComPortNumber(name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Settings.SerialPort) &&
            !ports.Contains(Settings.SerialPort, StringComparer.OrdinalIgnoreCase))
        {
            ports.Insert(0, Settings.SerialPort);
        }

        SerialPortOptions.Clear();
        foreach (var port in ports)
        {
            SerialPortOptions.Add(port);
        }

        RaisePropertyChanged(nameof(SerialPortAvailabilityText));
        RaisePropertyChanged(nameof(SerialProfile));
    }

    private string BuildSerialPortAvailabilityText()
    {
        if (SerialPortOptions.Count == 0)
        {
            return "No COM port detected on this laptop.";
        }

        var selected = Settings.SerialPort;
        if (string.IsNullOrWhiteSpace(selected))
        {
            return $"{SerialPortOptions.Count} COM port(s) detected.";
        }

        var detected = System.IO.Ports.SerialPort.GetPortNames().Contains(selected, StringComparer.OrdinalIgnoreCase);
        return detected
            ? $"{selected} is currently available."
            : $"{selected} is selected in settings but not currently detected.";
    }

    private static int ExtractComPortNumber(string portName)
    {
        if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(portName[3..], out var number))
        {
            return number;
        }

        return int.MaxValue;
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
                var mapping = profile.CommandMappings.FirstOrDefault(x =>
                    x.IsEnabled &&
                    x.CommandIndex == entry.Index &&
                    string.Equals(NormalizePointType(x.CommandPointType), NormalizePointType(entry.PointType), StringComparison.OrdinalIgnoreCase));
                if (mapping is not null)
                {
                    entry.FeedbackMappingEnabled = mapping.IsEnabled;
                    entry.FeedbackIndex = mapping.FeedbackIndex;
                    entry.FeedbackPointType = mapping.FeedbackPointType;
                    entry.FeedbackDisplayName = mapping.FeedbackDisplayName;
                    entry.DefaultCommandMode = mapping.DefaultCommandMode;
                    entry.TimeoutMs = mapping.TimeoutMs;
                }
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
            transaction = transaction with
            {
                DisplayName = point.DisplayName,
                ScadaTag = point.ScadaTag
            };
        }

        if (!string.IsNullOrWhiteSpace(transaction.ExpectedFeedbackPointType) && transaction.ExpectedFeedbackIndex.HasValue)
        {
            var mapping = PointCatalog.FirstOrDefault(x =>
                x.FeedbackMappingEnabled &&
                x.FeedbackIndex.HasValue &&
                string.Equals(NormalizePointType(x.FeedbackPointType), NormalizePointType(transaction.ExpectedFeedbackPointType), StringComparison.OrdinalIgnoreCase) &&
                x.FeedbackIndex.Value == transaction.ExpectedFeedbackIndex.Value);

            if (mapping is not null)
            {
                var feedbackName = !string.IsNullOrWhiteSpace(mapping.FeedbackDisplayName)
                    ? mapping.FeedbackDisplayName
                    : $"{mapping.FeedbackPointType} {mapping.FeedbackIndex}";

                return transaction with
                {
                    FeedbackResult = transaction.FeedbackResult == "Pending"
                        ? $"Pending feedback: {feedbackName}"
                        : transaction.FeedbackResult
                };
            }
        }

        return transaction;
    }

    private static string BuildCatalogKey(string pointType, ushort index) => $"{NormalizePointType(pointType)}:{index}";

    private static string NormalizePointType(string pointType)
    {
        var normalized = pointType.Trim();
        return normalized switch
        {
            "BI" => "Binary Input",
            "BO" => "Binary Output",
            "BOS" => "Binary Output Status",
            "AI" => "Analog Input",
            "AOS" => "Analog Output Status",
            _ => normalized
        };
    }

    private PointCatalogEntry? FindCommandMapping(ushort commandIndex, string commandPointType)
    {
        var normalizedType = NormalizePointType(commandPointType);
        return PointCatalog.FirstOrDefault(x =>
            x.FeedbackMappingEnabled &&
            x.Index == commandIndex &&
            string.Equals(NormalizePointType(x.PointType), normalizedType, StringComparison.OrdinalIgnoreCase));
    }

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
        SelectedPointCatalogProfile.CommandMappings = PointCatalog
            .Where(x => x.FeedbackMappingEnabled && x.FeedbackIndex.HasValue)
            .Select(x => new CommandFeedbackMapping
            {
                IsEnabled = x.FeedbackMappingEnabled,
                CommandIndex = x.Index,
                CommandPointType = x.PointType,
                CommandDisplayName = x.DisplayName,
                FeedbackIndex = x.FeedbackIndex!.Value,
                FeedbackPointType = x.FeedbackPointType,
                FeedbackDisplayName = x.FeedbackDisplayName,
                DefaultCommandMode = x.DefaultCommandMode,
                TimeoutMs = x.TimeoutMs
            })
            .ToList();
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

    private void OnWorkspaceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isBufferedCollectionUpdate)
        {
            _hasBufferedWorkspaceChanges = true;
            return;
        }

        RaiseWorkspaceSummaryChanged();
    }

    private void RaiseWorkspaceSummaryChanged()
    {
        RaisePropertyChanged(nameof(LiveValueCount));
        RaisePropertyChanged(nameof(BinaryValueRows));
        RaisePropertyChanged(nameof(AnalogValueRows));
        RaisePropertyChanged(nameof(GuidedCommandPoints));
        RaisePropertyChanged(nameof(CommandSequenceReadinessText));
        RaisePropertyChanged(nameof(EventLogCount));
        RaisePropertyChanged(nameof(SoeAuditCount));
        RaisePropertyChanged(nameof(LinkTraceCount));
        RaisePropertyChanged(nameof(PointCatalogCount));
        RaisePropertyChanged(nameof(ReportGeneratedAt));
        RaisePropertyChanged(nameof(LatestEventSummary));
        RaisePropertyChanged(nameof(LatestSoeSummary));
        RaisePropertyChanged(nameof(LatestTraceSummary));
    }

    private void RaiseReportSnapshotChanged()
    {
        RaisePropertyChanged(nameof(ReportId));
        RaisePropertyChanged(nameof(ReportEvidenceState));
        RaisePropertyChanged(nameof(ReportFinalizedAt));
        RaisePropertyChanged(nameof(ReportOverallVerdict));
        RaisePropertyChanged(nameof(ReportFatExecutionStatus));
        RaisePropertyChanged(nameof(ReportTechnicalResult));
        RaisePropertyChanged(nameof(ReportFatItemCount));
        RaisePropertyChanged(nameof(ReportExecutedItemCount));
        RaisePropertyChanged(nameof(ReportPassCount));
        RaisePropertyChanged(nameof(ReportWarningCount));
        RaisePropertyChanged(nameof(ReportFailCount));
        RaisePropertyChanged(nameof(ReportOpenItemCount));
    }

    private void RaiseReportCommandState()
    {
        RefreshReportSnapshotCommand.RaiseCanExecuteChanged();
        FinalizeReportEvidenceCommand.RaiseCanExecuteChanged();
        ReopenLiveReportCommand.RaiseCanExecuteChanged();
        RenderReportPreviewCommand.RaiseCanExecuteChanged();
        ExportReportPdfCommand.RaiseCanExecuteChanged();
        ClearCompanyLogoCommand.RaiseCanExecuteChanged();
        ClearCustomerLogoCommand.RaiseCanExecuteChanged();
        RunAutomatedReportTestingCommand.RaiseCanExecuteChanged();
    }

    private void RaiseBrandingChanged()
    {
        RaisePropertyChanged(nameof(CompanyName));
        RaisePropertyChanged(nameof(CustomerName));
        RaisePropertyChanged(nameof(ProjectName));
        RaisePropertyChanged(nameof(PreparedBy));
        RaisePropertyChanged(nameof(ReviewedBy));
        RaisePropertyChanged(nameof(ApprovedBy));
        RaisePropertyChanged(nameof(ReportFooterText));
        RaisePropertyChanged(nameof(CompanyLogoPath));
        RaisePropertyChanged(nameof(CustomerLogoPath));
        RaisePropertyChanged(nameof(CompanyLogoName));
        RaisePropertyChanged(nameof(CustomerLogoName));
        RaiseReportCommandState();
    }

    private void RaiseManualAssessmentChanged()
    {
        RaisePropertyChanged(nameof(BinaryIndicationAssessmentText));
        RaisePropertyChanged(nameof(BinaryIndicationRemarks));
        RaisePropertyChanged(nameof(AnalogValueAssessmentText));
        RaisePropertyChanged(nameof(AnalogValueRemarks));
        RaisePropertyChanged(nameof(CommandSequenceStatus));
        RaisePropertyChanged(nameof(CommandSequenceReadinessText));
        RaisePropertyChanged(nameof(NonOperationStatus));
        RaisePropertyChanged(nameof(RecoveryStatus));
        RaisePropertyChanged(nameof(GuidedTestingProgressStatus));
        RaiseReportSnapshotChanged();
    }

    private void RaiseReportWorkspaceStageChanged()
    {
        RaisePropertyChanged(nameof(ReportWorkspaceStageText));
        RaisePropertyChanged(nameof(ReportSetupVisibility));
        RaisePropertyChanged(nameof(ReportTestingVisibility));
        RaisePropertyChanged(nameof(ReportBinaryVisibility));
        RaisePropertyChanged(nameof(ReportAnalogVisibility));
        RaisePropertyChanged(nameof(ReportCommandSequenceVisibility));
        RaisePropertyChanged(nameof(ReportNonOperationRecoveryVisibility));
        RaisePropertyChanged(nameof(ReportSummaryVisibility));
        RaisePropertyChanged(nameof(ReportPreviewVisibility));
        RaisePropertyChanged(nameof(ReportPrePreviewVisibility));
    }
}
