using System.Collections.ObjectModel;
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
    private Dnp3SimulatorSignal? _selectedSignal;
    private string _statusText = "Ready";
    private string _runtimeState = "Stopped";
    private bool _isRunning;

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _outstationService = new Dnp3OutstationService(AppendLog);
        _outstationService.StateChanged += state =>
        {
            _dispatcher.Invoke(() =>
            {
                RuntimeState = state;
                StatusText = state;
            });
        };
        _outstationService.SignalCommanded += ApplyCommandedSignal;

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _animationTimer.Tick += AnimationTimer_Tick;

        Connection = new Dnp3SlaveConnectionSettings();
        Signals = new ObservableCollection<Dnp3SimulatorSignal>();
        RuntimeLog = new ObservableCollection<RuntimeLogEntry>();

        TransportOptions = Enum.GetValues(typeof(Dnp3SlaveTransportType)).Cast<Dnp3SlaveTransportType>().ToArray();
        PointTypeOptions = Enum.GetValues(typeof(Dnp3OutstationPointType)).Cast<Dnp3OutstationPointType>().ToArray();
        EventClassOptions = Enum.GetValues(typeof(Dnp3EventClassModel)).Cast<Dnp3EventClassModel>().ToArray();
        AnalogAnimationOptions = Enum.GetValues(typeof(AnalogAnimationKind)).Cast<AnalogAnimationKind>().ToArray();
        DiscreteAnimationOptions = Enum.GetValues(typeof(DiscreteAnimationKind)).Cast<DiscreteAnimationKind>().ToArray();

        StartRuntimeCommand = new RelayCommand(_ => StartRuntime(), _ => !IsRunning && Signals.Count > 0);
        StopRuntimeCommand = new RelayCommand(_ => StopRuntime(), _ => IsRunning);
        AddPointCommand = new RelayCommand(_ => AddPoint(), _ => !IsRunning);
        RemovePointCommand = new RelayCommand(_ => RemovePoint(), _ => !IsRunning && SelectedSignal is not null);
        ToggleSelectedCommand = new RelayCommand(_ => ToggleSelected(), _ => SelectedSignal?.IsBinaryLike == true);
        NudgeAnalogCommand = new RelayCommand(_ => NudgeAnalog(), _ => SelectedSignal?.IsAnalogLike == true);
        ClearLogCommand = new RelayCommand(_ => RuntimeLog.Clear());

        SeedDefaultSignals();
        StatusText = "Seeded default DNP3 points. Start runtime to expose TCP/serial outstation.";
    }

    public Dnp3SlaveConnectionSettings Connection { get; }
    public ObservableCollection<Dnp3SimulatorSignal> Signals { get; }
    public ObservableCollection<RuntimeLogEntry> RuntimeLog { get; }

    public Dnp3SlaveTransportType[] TransportOptions { get; }
    public Dnp3OutstationPointType[] PointTypeOptions { get; }
    public Dnp3EventClassModel[] EventClassOptions { get; }
    public AnalogAnimationKind[] AnalogAnimationOptions { get; }
    public DiscreteAnimationKind[] DiscreteAnimationOptions { get; }

    public RelayCommand StartRuntimeCommand { get; }
    public RelayCommand StopRuntimeCommand { get; }
    public RelayCommand AddPointCommand { get; }
    public RelayCommand RemovePointCommand { get; }
    public RelayCommand ToggleSelectedCommand { get; }
    public RelayCommand NudgeAnalogCommand { get; }
    public RelayCommand ClearLogCommand { get; }

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

    public string ConnectionSummary =>
        Connection.Transport == Dnp3SlaveTransportType.TcpServer
            ? $"TCP server on {Connection.Endpoint} | Master {Connection.MasterAddress} -> Outstation {Connection.OutstationAddress}"
            : $"Serial {Connection.SerialPort} | Master {Connection.MasterAddress} -> Outstation {Connection.OutstationAddress}";

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

    private void SeedDefaultSignals()
    {
        Signals.Clear();
        Signals.Add(new Dnp3SimulatorSignal
        {
            Index = 0,
            Label = "52A Breaker Closed",
            PointType = Dnp3OutstationPointType.BinaryInput,
            EventClass = Dnp3EventClassModel.Class1,
            BoolValue = false,
            DiscreteAnimation = DiscreteAnimationKind.Toggle,
            ToggleIntervalSeconds = 15,
            Notes = "Binary indication for breaker closed status"
        });
        Signals.Add(new Dnp3SimulatorSignal
        {
            Index = 1,
            Label = "Trip Alarm",
            PointType = Dnp3OutstationPointType.BinaryInput,
            EventClass = Dnp3EventClassModel.Class1,
            BoolValue = false,
            Notes = "Binary alarm indication"
        });
        Signals.Add(new Dnp3SimulatorSignal
        {
            Index = 0,
            Label = "Phase Current A",
            PointType = Dnp3OutstationPointType.AnalogInput,
            EventClass = Dnp3EventClassModel.Class2,
            AnalogValue = 125,
            AnalogMin = 110,
            AnalogMax = 145,
            AnalogStep = 0.5,
            AnalogAnimation = AnalogAnimationKind.PingPong,
            AnimationIntervalMs = 1000,
            Notes = "Analog measurement animation"
        });
        Signals.Add(new Dnp3SimulatorSignal
        {
            Index = 0,
            Label = "Close Command Feedback",
            PointType = Dnp3OutstationPointType.BinaryOutputStatus,
            EventClass = Dnp3EventClassModel.Class1,
            BoolValue = false,
            Notes = "Updated by CROB lifecycle"
        });
        Signals.Add(new Dnp3SimulatorSignal
        {
            Index = 0,
            Label = "Tap Changer Target",
            PointType = Dnp3OutstationPointType.AnalogOutputStatus,
            EventClass = Dnp3EventClassModel.Class2,
            AnalogValue = 0,
            AnalogMin = -16,
            AnalogMax = 16,
            AnalogStep = 1,
            Notes = "Updated by analog output command"
        });
        SelectedSignal = Signals.FirstOrDefault();
    }

    private void StartRuntime()
    {
        try
        {
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
        _dispatcher.Invoke(() =>
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
        _dispatcher.Invoke(() =>
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
        ToggleSelectedCommand.RaiseCanExecuteChanged();
        NudgeAnalogCommand.RaiseCanExecuteChanged();
    }
}
