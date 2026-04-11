using System.Globalization;
using Dnp3SlaveSimulator.ViewModels;

namespace Dnp3SlaveSimulator.Models;

public sealed class Dnp3SimulatorSignal : ViewModelBase
{
    private ushort _index;
    private string _label = "New Point";
    private Dnp3OutstationPointType _pointType = Dnp3OutstationPointType.BinaryInput;
    private Dnp3EventClassModel _eventClass = Dnp3EventClassModel.Class1;
    private bool _isEnabled = true;
    private bool _boolValue;
    private double _analogValue;
    private double _analogMin;
    private double _analogMax = 100;
    private double _analogStep = 1;
    private int _animationIntervalMs = 1000;
    private int _toggleIntervalSeconds = 5;
    private AnalogAnimationKind _analogAnimation = AnalogAnimationKind.None;
    private DiscreteAnimationKind _discreteAnimation = DiscreteAnimationKind.None;
    private bool _useTimestamp = true;
    private string _notes = string.Empty;
    private string _lastCommandStatus = "-";
    private string _lastCommandDetail = "-";
    private DateTime _lastUpdatedLocal = DateTime.Now;
    private SignalEventTimestampState _eventTimestamp = SignalEventTimestampState.Invalid();
    private DateTime _nextAnimationAt = DateTime.Now;
    private bool _ascending = true;

    public ushort Index { get => _index; set => SetProperty(ref _index, value); }
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public Dnp3OutstationPointType PointType { get => _pointType; set => SetProperty(ref _pointType, value); }
    public Dnp3EventClassModel EventClass { get => _eventClass; set => SetProperty(ref _eventClass, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public bool BoolValue { get => _boolValue; set => SetProperty(ref _boolValue, value); }
    public double AnalogValue { get => _analogValue; set => SetProperty(ref _analogValue, value); }
    public double AnalogMin { get => _analogMin; set => SetProperty(ref _analogMin, value); }
    public double AnalogMax { get => _analogMax; set => SetProperty(ref _analogMax, value); }
    public double AnalogStep { get => _analogStep; set => SetProperty(ref _analogStep, value); }
    public int AnimationIntervalMs { get => _animationIntervalMs; set => SetProperty(ref _animationIntervalMs, value); }
    public int ToggleIntervalSeconds { get => _toggleIntervalSeconds; set => SetProperty(ref _toggleIntervalSeconds, value); }
    public AnalogAnimationKind AnalogAnimation { get => _analogAnimation; set => SetProperty(ref _analogAnimation, value); }
    public DiscreteAnimationKind DiscreteAnimation { get => _discreteAnimation; set => SetProperty(ref _discreteAnimation, value); }
    public bool UseTimestamp { get => _useTimestamp; set => SetProperty(ref _useTimestamp, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string LastCommandStatus { get => _lastCommandStatus; set => SetProperty(ref _lastCommandStatus, value); }
    public string LastCommandDetail { get => _lastCommandDetail; set => SetProperty(ref _lastCommandDetail, value); }
    public DateTime LastUpdatedLocal { get => _lastUpdatedLocal; set => SetProperty(ref _lastUpdatedLocal, value); }
    public SignalEventTimestampState EventTimestamp { get => _eventTimestamp; set => SetProperty(ref _eventTimestamp, value); }

    public bool IsBinaryLike => PointType is Dnp3OutstationPointType.BinaryInput or Dnp3OutstationPointType.BinaryOutputStatus;
    public bool IsAnalogLike => PointType is Dnp3OutstationPointType.AnalogInput or Dnp3OutstationPointType.AnalogOutputStatus;
    public bool IsCommandable => PointType is Dnp3OutstationPointType.BinaryOutputStatus or Dnp3OutstationPointType.AnalogOutputStatus;
    public string RuntimeValueText => IsBinaryLike ? (BoolValue ? "ON" : "OFF") : AnalogValue.ToString("0.###", CultureInfo.InvariantCulture);

    public Dnp3SimulatorSignal Clone()
    {
        return (Dnp3SimulatorSignal)MemberwiseClone();
    }

    public void CaptureEdgeTimestamp(DateTime now)
    {
        LastUpdatedLocal = now;
        EventTimestamp = UseTimestamp ? SignalEventTimestampState.Synchronized(now) : SignalEventTimestampState.Invalid();
    }

    public bool TryAdvanceAnimation(DateTime now)
    {
        if (!IsEnabled || now < _nextAnimationAt)
        {
            return false;
        }

        var changed = false;

        if (IsBinaryLike && DiscreteAnimation == DiscreteAnimationKind.Toggle)
        {
            BoolValue = !BoolValue;
            changed = true;
            _nextAnimationAt = now.AddSeconds(Math.Max(1, ToggleIntervalSeconds));
        }
        else if (IsAnalogLike && AnalogAnimation != AnalogAnimationKind.None)
        {
            var step = Math.Abs(AnalogStep) < 0.0001d ? 1d : Math.Abs(AnalogStep);
            var next = _ascending ? AnalogValue + step : AnalogValue - step;

            if (AnalogAnimation == AnalogAnimationKind.RampLoop)
            {
                if (next > AnalogMax)
                {
                    next = AnalogMin;
                }
            }
            else
            {
                if (next >= AnalogMax)
                {
                    next = AnalogMax;
                    _ascending = false;
                }
                else if (next <= AnalogMin)
                {
                    next = AnalogMin;
                    _ascending = true;
                }
            }

            AnalogValue = next;
            changed = true;
            _nextAnimationAt = now.AddMilliseconds(Math.Max(100, AnimationIntervalMs));
        }

        if (changed)
        {
            CaptureEdgeTimestamp(now);
            NotifyPropertyChanged(nameof(RuntimeValueText));
        }

        return changed;
    }
}
