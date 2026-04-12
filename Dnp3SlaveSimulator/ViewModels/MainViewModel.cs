using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using Dnp3SlaveSimulator.Models;
using Dnp3SlaveSimulator.Services;

namespace Dnp3SlaveSimulator.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _animationTimer;
    private readonly Dnp3OutstationService _outstationService;
    private readonly JsonSerializerOptions _profileSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private Dnp3SimulatorSignal? _selectedSignal;
    private SignalDatabaseProfile? _selectedSignalProfile;
    private string _statusText = "Ready";
    private string _runtimeState = "Stopped";
    private bool _isRunning;

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _outstationService = new Dnp3OutstationService(AppendLog);
        _outstationService.StateChanged += state =>
        {
            _ = _dispatcher.BeginInvoke(() =>
            {
                RuntimeState = state;
                StatusText = state;
            });
        };
        _outstationService.SignalCommanded += ApplyCommandedSignal;

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _animationTimer.Tick += AnimationTimer_Tick;

        Connection = new Dnp3SlaveConnectionSettings();
        Connection.PropertyChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(ConnectionSummary));
            RaisePropertyChanged(nameof(SerialPortAvailabilityText));
            RaisePropertyChanged(nameof(IsTcpTransport));
            RaisePropertyChanged(nameof(IsSerialTransport));
        };
        Signals = new ObservableCollection<Dnp3SimulatorSignal>();
        SignalProfiles = LoadSignalProfiles();
        RuntimeLog = new ObservableCollection<RuntimeLogEntry>();
        SerialPortOptions = new ObservableCollection<string>();

        TransportOptions = Enum.GetValues(typeof(Dnp3SlaveTransportType)).Cast<Dnp3SlaveTransportType>().ToArray();
        PointTypeOptions = Enum.GetValues(typeof(Dnp3OutstationPointType)).Cast<Dnp3OutstationPointType>().ToArray();
        EventClassOptions = Enum.GetValues(typeof(Dnp3EventClassModel)).Cast<Dnp3EventClassModel>().ToArray();
        SerialDataBitOptions = Enum.GetValues(typeof(dnp3.DataBits)).Cast<dnp3.DataBits>().ToArray();
        SerialStopBitOptions = Enum.GetValues(typeof(dnp3.StopBits)).Cast<dnp3.StopBits>().ToArray();
        SerialParityOptions = Enum.GetValues(typeof(dnp3.Parity)).Cast<dnp3.Parity>().ToArray();
        SerialFlowControlOptions = Enum.GetValues(typeof(dnp3.FlowControl)).Cast<dnp3.FlowControl>().ToArray();
        AnalogAnimationOptions = Enum.GetValues(typeof(AnalogAnimationKind)).Cast<AnalogAnimationKind>().ToArray();
        DiscreteAnimationOptions = Enum.GetValues(typeof(DiscreteAnimationKind)).Cast<DiscreteAnimationKind>().ToArray();
        BinaryCommandBehaviorOptions = Enum.GetValues(typeof(BinaryCommandBehavior)).Cast<BinaryCommandBehavior>().ToArray();

        StartRuntimeCommand = new RelayCommand(_ => StartRuntime(), _ => !IsRunning && Signals.Count > 0);
        StopRuntimeCommand = new RelayCommand(_ => StopRuntime(), _ => IsRunning);
        AddPointCommand = new RelayCommand(_ => AddPoint(), _ => !IsRunning);
        RemovePointCommand = new RelayCommand(_ => RemovePoint(), _ => !IsRunning && SelectedSignal is not null);
        SaveProfileCommand = new RelayCommand(_ => SaveProfile(), _ => !IsRunning && SelectedSignalProfile is not null);
        ReloadProfileCommand = new RelayCommand(_ => ReloadProfile(), _ => !IsRunning && SelectedSignalProfile is not null);
        ToggleSelectedCommand = new RelayCommand(_ => ToggleSelected(), _ => SelectedSignal?.IsBinaryLike == true);
        NudgeAnalogCommand = new RelayCommand(_ => NudgeAnalog(), _ => SelectedSignal?.IsAnalogLike == true);
        ClearLogCommand = new RelayCommand(_ => RuntimeLog.Clear());
        RefreshSerialPortsCommand = new RelayCommand(_ => RefreshSerialPorts(), _ => !IsRunning);

        RefreshSerialPorts();

        SelectedSignalProfile = SignalProfiles.FirstOrDefault();
        if (SelectedSignalProfile is null)
        {
            StatusText = "No signal profile found. Add JSON profiles under MetadataProfiles.";
        }
    }

    public Dnp3SlaveConnectionSettings Connection { get; }
    public ObservableCollection<Dnp3SimulatorSignal> Signals { get; }
    public ObservableCollection<SignalDatabaseProfile> SignalProfiles { get; }
    public ObservableCollection<RuntimeLogEntry> RuntimeLog { get; }
    public ObservableCollection<string> SerialPortOptions { get; }

    public Dnp3SlaveTransportType[] TransportOptions { get; }
    public Dnp3OutstationPointType[] PointTypeOptions { get; }
    public Dnp3EventClassModel[] EventClassOptions { get; }
    public dnp3.DataBits[] SerialDataBitOptions { get; }
    public dnp3.StopBits[] SerialStopBitOptions { get; }
    public dnp3.Parity[] SerialParityOptions { get; }
    public dnp3.FlowControl[] SerialFlowControlOptions { get; }
    public AnalogAnimationKind[] AnalogAnimationOptions { get; }
    public DiscreteAnimationKind[] DiscreteAnimationOptions { get; }
    public BinaryCommandBehavior[] BinaryCommandBehaviorOptions { get; }

    public RelayCommand StartRuntimeCommand { get; }
    public RelayCommand StopRuntimeCommand { get; }
    public RelayCommand AddPointCommand { get; }
    public RelayCommand RemovePointCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand ReloadProfileCommand { get; }
    public RelayCommand ToggleSelectedCommand { get; }
    public RelayCommand NudgeAnalogCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand RefreshSerialPortsCommand { get; }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string RuntimeState
    {
        get => _runtimeState;
        set => SetProperty(ref _runtimeState, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaisePropertyChanged(nameof(CanEditSettings));
                RaisePropertyChanged(nameof(ConnectionSummary));
                RefreshCommands();
            }
        }
    }

    public bool CanEditSettings => !IsRunning;
    public bool IsTcpTransport => Connection.Transport == Dnp3SlaveTransportType.TcpServer;
    public bool IsSerialTransport => Connection.Transport == Dnp3SlaveTransportType.Serial;

    public string SignalProfileName => SelectedSignalProfile?.Name ?? "No Signal Profile";
    public string SerialPortAvailabilityText => BuildSerialPortAvailabilityText();

    public string ConnectionSummary =>
        Connection.Transport == Dnp3SlaveTransportType.TcpServer
            ? $"TCP server on {Connection.Endpoint} | Master {Connection.MasterAddress} -> Outstation {Connection.OutstationAddress} | Profile {SignalProfileName} | Unsol {(Connection.EnableUnsolicited ? DescribeUnsolicitedClasses() : "Off")}"
            : $"Serial {Connection.GetSerialSummary()} | Master {Connection.MasterAddress} -> Outstation {Connection.OutstationAddress} | Profile {SignalProfileName} | Unsol {(Connection.EnableUnsolicited ? DescribeUnsolicitedClasses() : "Off")}";

    public SignalDatabaseProfile? SelectedSignalProfile
    {
        get => _selectedSignalProfile;
        set
        {
            if (SetProperty(ref _selectedSignalProfile, value))
            {
                ApplySignalProfile(value);
                RaisePropertyChanged(nameof(SignalProfileName));
                RaisePropertyChanged(nameof(ConnectionSummary));
                RefreshCommands();
            }
        }
    }

    public Dnp3SimulatorSignal? SelectedSignal
    {
        get => _selectedSignal;
        set
        {
            if (SetProperty(ref _selectedSignal, value))
            {
                RefreshCommands();
            }
        }
    }

    private ObservableCollection<SignalDatabaseProfile> LoadSignalProfiles()
    {
        var result = new ObservableCollection<SignalDatabaseProfile>();
        var profileDirectory = Path.Combine(AppContext.BaseDirectory, "MetadataProfiles");
        if (!Directory.Exists(profileDirectory))
        {
            return result;
        }

        foreach (var file in Directory.GetFiles(profileDirectory, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(file);
            var profile = JsonSerializer.Deserialize<SignalDatabaseProfile>(json, _profileSerializerOptions);
            if (profile is null)
            {
                continue;
            }

            profile.FilePath = file;
            result.Add(profile);
        }

        return result;
    }

    private void ApplySignalProfile(SignalDatabaseProfile? profile)
    {
        Signals.Clear();
        if (profile is not null)
        {
            ApplyCommunicationProfile(profile.Communication);

            foreach (var signal in profile.Signals)
            {
                var mapping = profile.CommandMappings.FirstOrDefault(x =>
                    x.IsEnabled &&
                    x.CommandIndex == signal.Index &&
                    x.CommandPointType == signal.PointType);
                if (mapping is not null)
                {
                    signal.FeedbackMappingEnabled = mapping.IsEnabled;
                    signal.FeedbackIndex = mapping.FeedbackIndex;
                    signal.FeedbackPointType = mapping.FeedbackPointType;
                    signal.FeedbackDisplayName = mapping.FeedbackDisplayName;
                    signal.DefaultCommandMode = "DirectOperate";
                    signal.FeedbackTimeoutMs = 5000;
                    signal.BinaryCommand.FeedbackDelayMs = mapping.FeedbackDelayMs;
                }
                Signals.Add(signal);
            }
        }

        ApplyCommandMappingsToSignals();
        SelectedSignal = Signals.FirstOrDefault();
        StatusText = profile is null
            ? "No signal profile selected."
            : $"Loaded signal profile: {profile.Name}";
    }

    private void SaveProfile()
    {
        if (SelectedSignalProfile is null || string.IsNullOrWhiteSpace(SelectedSignalProfile.FilePath))
        {
            return;
        }

        ApplyCommandMappingsToSignals();
        SelectedSignalProfile.Communication = BuildCommunicationProfile();
        SelectedSignalProfile.Signals = Signals.ToList();
        SelectedSignalProfile.CommandMappings = Signals
            .Where(x => x.FeedbackMappingEnabled && x.FeedbackIndex.HasValue)
            .Select(x => new CommandFeedbackMapping
            {
                IsEnabled = x.FeedbackMappingEnabled,
                CommandIndex = x.Index,
                CommandPointType = x.PointType,
                CommandDisplayName = x.Label,
                FeedbackIndex = x.FeedbackIndex!.Value,
                FeedbackPointType = x.FeedbackPointType,
                FeedbackDisplayName = x.FeedbackDisplayName,
                FeedbackDelayMs = x.BinaryCommand.FeedbackDelayMs
            })
            .ToList();
        var json = JsonSerializer.Serialize(SelectedSignalProfile, _profileSerializerOptions);
        File.WriteAllText(SelectedSignalProfile.FilePath, json);
        StatusText = $"Saved signal profile: {SelectedSignalProfile.Name}";
        AppendLog(new RuntimeLogEntry
        {
            TimestampLocal = DateTime.Now,
            Category = "Profile",
            Message = $"Saved {SelectedSignalProfile.Name}"
        });
    }

    private void ReloadProfile()
    {
        if (SelectedSignalProfile is null || string.IsNullOrWhiteSpace(SelectedSignalProfile.FilePath) || !File.Exists(SelectedSignalProfile.FilePath))
        {
            return;
        }

        var json = File.ReadAllText(SelectedSignalProfile.FilePath);
        var reloaded = JsonSerializer.Deserialize<SignalDatabaseProfile>(json, _profileSerializerOptions);
        if (reloaded is null)
        {
            return;
        }

        reloaded.FilePath = SelectedSignalProfile.FilePath;
        var profileIndex = SignalProfiles.IndexOf(SelectedSignalProfile);
        SignalProfiles[profileIndex] = reloaded;
        SelectedSignalProfile = reloaded;
        AppendLog(new RuntimeLogEntry
        {
            TimestampLocal = DateTime.Now,
            Category = "Profile",
            Message = $"Reloaded {SelectedSignalProfile.Name}"
        });
    }

    private void StartRuntime()
    {
        try
        {
            var errors = Connection.Validate();
            if (errors.Count != 0)
            {
                StatusText = string.Join(" ", errors);
                AppendLog(new RuntimeLogEntry
                {
                    TimestampLocal = DateTime.Now,
                    Category = "Validation",
                    Message = StatusText
                });
                return;
            }

            ApplyCommandMappingsToSignals();
            SeedStartupTimestamps();
            _outstationService.Start(Connection, Signals);
            _animationTimer.Start();
            IsRunning = true;
            StatusText = "Outstation runtime started.";
            AppendLog(new RuntimeLogEntry
            {
                TimestampLocal = DateTime.Now,
                Category = "Runtime",
                Message = $"Started with {Signals.Count} configured points"
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to start: {ex.Message}";
            AppendLog(new RuntimeLogEntry
            {
                TimestampLocal = DateTime.Now,
                Category = "Error",
                Message = ex.ToString()
            });
        }
    }

    private void StopRuntime()
    {
        _animationTimer.Stop();
        _outstationService.Stop();
        IsRunning = false;
        StatusText = "Runtime stopped.";
        AppendLog(new RuntimeLogEntry
        {
            TimestampLocal = DateTime.Now,
            Category = "Runtime",
            Message = "Stopped outstation runtime"
        });
    }

    private void AddPoint()
    {
        var nextIndex = Signals.Where(x => x.PointType == Dnp3OutstationPointType.BinaryInput).Select(x => (int)x.Index).DefaultIfEmpty(-1).Max() + 1;
        var signal = new Dnp3SimulatorSignal
        {
            Index = (ushort)Math.Max(0, nextIndex),
            Label = $"New Point {nextIndex}",
            PointType = Dnp3OutstationPointType.BinaryInput,
            EventClass = Dnp3EventClassModel.Class1,
            BoolValue = false,
            ToggleIntervalSeconds = 5
        };
        Signals.Add(signal);
        SelectedSignal = signal;
        StatusText = "New point added.";
        RefreshCommands();
    }

    private void RemovePoint()
    {
        if (SelectedSignal is null)
        {
            return;
        }

        Signals.Remove(SelectedSignal);
        SelectedSignal = Signals.FirstOrDefault();
        StatusText = "Selected point removed.";
        RefreshCommands();
    }

    private void ToggleSelected()
    {
        if (SelectedSignal is null || !SelectedSignal.IsBinaryLike)
        {
            return;
        }

        SelectedSignal.BoolValue = !SelectedSignal.BoolValue;
        SelectedSignal.CaptureEdgeTimestamp(DateTime.Now);
        SelectedSignal.NotifyPropertyChanged(nameof(Dnp3SimulatorSignal.RuntimeValueText));

        if (IsRunning)
        {
            _outstationService.PublishSignalValue(SelectedSignal);
        }

        AppendLog(new RuntimeLogEntry
        {
            TimestampLocal = DateTime.Now,
            Category = "Manual",
            Message = $"{SelectedSignal.Label} toggled to {(SelectedSignal.BoolValue ? "ON" : "OFF")} with {(SelectedSignal.UseTimestamp ? "edge timestamp" : "invalid/no-time")}"
        });
    }

    private void NudgeAnalog()
    {
        if (SelectedSignal is null || !SelectedSignal.IsAnalogLike)
        {
            return;
        }

        var step = Math.Abs(SelectedSignal.AnalogStep) < 0.001d ? 1d : SelectedSignal.AnalogStep;
        SelectedSignal.AnalogValue += step;
        if (SelectedSignal.AnalogValue > SelectedSignal.AnalogMax)
        {
            SelectedSignal.AnalogValue = SelectedSignal.AnalogMin;
        }

        SelectedSignal.CaptureEdgeTimestamp(DateTime.Now);
        SelectedSignal.NotifyPropertyChanged(nameof(Dnp3SimulatorSignal.RuntimeValueText));

        if (IsRunning)
        {
            _outstationService.PublishSignalValue(SelectedSignal);
        }

        AppendLog(new RuntimeLogEntry
        {
            TimestampLocal = DateTime.Now,
            Category = "Manual",
            Message = $"{SelectedSignal.Label} adjusted to {SelectedSignal.AnalogValue:0.###} with {(SelectedSignal.UseTimestamp ? "edge timestamp" : "invalid/no-time")}"
        });
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        foreach (var signal in Signals)
        {
            if (!signal.TryAdvanceAnimation(now))
            {
                continue;
            }

            _outstationService.PublishSignalValue(signal);
            AppendLog(new RuntimeLogEntry
            {
                TimestampLocal = now,
                Category = "Animation",
                Message = $"{signal.Label} -> {signal.RuntimeValueText}"
            });
        }
    }

    private void ApplyCommandedSignal(Dnp3SimulatorSignal commandedSignal)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            var match = Signals.FirstOrDefault(x => x.PointType == commandedSignal.PointType && x.Index == commandedSignal.Index);
            if (match is null)
            {
                return;
            }

            match.BoolValue = commandedSignal.BoolValue;
            match.AnalogValue = commandedSignal.AnalogValue;
            match.LastUpdatedLocal = commandedSignal.LastUpdatedLocal;
            match.LastCommandStatus = commandedSignal.LastCommandStatus;
            match.LastCommandDetail = commandedSignal.LastCommandDetail;
            match.NotifyPropertyChanged(nameof(Dnp3SimulatorSignal.RuntimeValueText));
        });
    }

    private void AppendLog(RuntimeLogEntry entry)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            RuntimeLog.Insert(0, entry);
            while (RuntimeLog.Count > 500)
            {
                RuntimeLog.RemoveAt(RuntimeLog.Count - 1);
            }
        });
    }

    private void RefreshCommands()
    {
        StartRuntimeCommand.RaiseCanExecuteChanged();
        StopRuntimeCommand.RaiseCanExecuteChanged();
        AddPointCommand.RaiseCanExecuteChanged();
        RemovePointCommand.RaiseCanExecuteChanged();
        SaveProfileCommand.RaiseCanExecuteChanged();
        ReloadProfileCommand.RaiseCanExecuteChanged();
        ToggleSelectedCommand.RaiseCanExecuteChanged();
        NudgeAnalogCommand.RaiseCanExecuteChanged();
    }

    private void ApplyCommandMappingsToSignals()
    {
        foreach (var signal in Signals.Where(x => x.PointType == Dnp3OutstationPointType.BinaryOutputStatus))
        {
            signal.BinaryCommand.IsEnabled = false;
            signal.BinaryCommand.FeedbackIndex = signal.Index;
            signal.BinaryCommand.FeedbackPointType = Dnp3OutstationPointType.BinaryOutputStatus;
            signal.BinaryCommand.FeedbackDelayMs = 800;
        }

        foreach (var commandSignal in Signals.Where(x => x.FeedbackMappingEnabled && x.FeedbackIndex.HasValue))
        {
            commandSignal.BinaryCommand.IsEnabled = true;
            commandSignal.BinaryCommand.FeedbackIndex = commandSignal.FeedbackIndex!.Value;
            commandSignal.BinaryCommand.FeedbackPointType = commandSignal.FeedbackPointType;
        }
    }

    private void SeedStartupTimestamps()
    {
        var now = DateTime.Now;
        foreach (var signal in Signals.Where(ShouldSeedStartupTimestamp))
        {
            signal.CaptureEdgeTimestamp(now);
            signal.NotifyPropertyChanged(nameof(Dnp3SimulatorSignal.RuntimeValueText));
        }

        AppendLog(new RuntimeLogEntry
        {
            TimestampLocal = now,
            Category = "Startup",
            Message = $"Seeded startup timestamps for {Signals.Count(ShouldSeedStartupTimestamp)} enabled event-capable points"
        });
    }

    private static bool ShouldSeedStartupTimestamp(Dnp3SimulatorSignal signal)
    {
        return signal.IsEnabled &&
               signal.UseTimestamp &&
               signal.PointType is Dnp3OutstationPointType.BinaryInput
                   or Dnp3OutstationPointType.AnalogInput
                   or Dnp3OutstationPointType.BinaryOutputStatus
                   or Dnp3OutstationPointType.AnalogOutputStatus;
    }

    private void ApplyCommunicationProfile(SlaveCommunicationProfile? communication)
    {
        communication ??= new SlaveCommunicationProfile();
        Connection.EnableUnsolicited = communication.EnableUnsolicited;
        Connection.UnsolicitedClass1 = communication.UnsolicitedClass1;
        Connection.UnsolicitedClass2 = communication.UnsolicitedClass2;
        Connection.UnsolicitedClass3 = communication.UnsolicitedClass3;
        RaisePropertyChanged(nameof(ConnectionSummary));
    }

    private SlaveCommunicationProfile BuildCommunicationProfile()
    {
        return new SlaveCommunicationProfile
        {
            EnableUnsolicited = Connection.EnableUnsolicited,
            UnsolicitedClass1 = Connection.UnsolicitedClass1,
            UnsolicitedClass2 = Connection.UnsolicitedClass2,
            UnsolicitedClass3 = Connection.UnsolicitedClass3
        };
    }

    private string DescribeUnsolicitedClasses()
    {
        var classes = new List<string>();
        if (Connection.UnsolicitedClass1)
        {
            classes.Add("C1");
        }

        if (Connection.UnsolicitedClass2)
        {
            classes.Add("C2");
        }

        if (Connection.UnsolicitedClass3)
        {
            classes.Add("C3");
        }

        return classes.Count == 0 ? "On (none)" : $"On ({string.Join("/", classes)})";
    }

    private void RefreshSerialPorts()
    {
        var ports = System.IO.Ports.SerialPort.GetPortNames()
            .OrderBy(name => ExtractComPortNumber(name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Connection.SerialPort) &&
            !ports.Contains(Connection.SerialPort, StringComparer.OrdinalIgnoreCase))
        {
            ports.Insert(0, Connection.SerialPort);
        }

        SerialPortOptions.Clear();
        foreach (var port in ports)
        {
            SerialPortOptions.Add(port);
        }

        RaisePropertyChanged(nameof(SerialPortAvailabilityText));
        RaisePropertyChanged(nameof(ConnectionSummary));
    }

    private string BuildSerialPortAvailabilityText()
    {
        if (SerialPortOptions.Count == 0)
        {
            return "No COM port detected on this laptop.";
        }

        var selected = Connection.SerialPort;
        if (string.IsNullOrWhiteSpace(selected))
        {
            return $"{SerialPortOptions.Count} COM port(s) detected.";
        }

        var detected = System.IO.Ports.SerialPort.GetPortNames().Contains(selected, StringComparer.OrdinalIgnoreCase);
        return detected
            ? $"{selected} is currently available."
            : $"{selected} is selected from profile but not currently detected.";
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
}
