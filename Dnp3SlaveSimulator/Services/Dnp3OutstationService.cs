using Dnp3SlaveSimulator.Models;
using dnp3;

namespace Dnp3SlaveSimulator.Services;

public sealed class Dnp3OutstationService : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<(Dnp3OutstationPointType, ushort), Dnp3SimulatorSignal> _runtimeSignals = new();
    private readonly Action<RuntimeLogEntry>? _logSink;
    private readonly List<CancellationTokenSource> _pendingCommandFeedback = new();
    private Runtime? _runtime;
    private Outstation? _outstation;
    private OutstationServer? _server;
    private bool _isRunning;

    public Dnp3OutstationService(Action<RuntimeLogEntry>? logSink = null)
    {
        _logSink = logSink;
    }

    public event Action<string>? StateChanged;
    public event Action<Dnp3SimulatorSignal>? SignalCommanded;

    public bool IsRunning => _isRunning;

    public void Start(Dnp3SlaveConnectionSettings settings, IEnumerable<Dnp3SimulatorSignal> signals)
    {
        Stop();

        lock (_sync)
        {
            _runtime = new Runtime(new RuntimeConfig { NumCoreThreads = 4 });
            var config = GetOutstationConfig(settings);
            var signalList = signals.Select(x => x.Clone()).ToList();
            IndexSignals(signalList);

            if (settings.Transport == Dnp3SlaveTransportType.TcpServer)
            {
                _server = OutstationServer.CreateTcpServer(_runtime, LinkErrorMode.Close, settings.Endpoint);
                _outstation = _server.AddOutstation(
                    config,
                    new OutstationApplicationAdapter(this),
                    new OutstationInformationAdapter(this),
                    new ControlHandlerAdapter(this),
                    new ConnectionStateListenerAdapter(this),
                    AddressFilter.Any());
                _server.Bind();
                PublishLog("Runtime", $"TCP server bound at {settings.Endpoint}");
            }
            else
            {
                _outstation = Outstation.CreateSerialSession2(
                    _runtime,
                    settings.SerialPort,
                    new SerialSettings(),
                    TimeSpan.FromSeconds(Math.Max(1, settings.PortRetrySeconds)),
                    config,
                    new OutstationApplicationAdapter(this),
                    new OutstationInformationAdapter(this),
                    new ControlHandlerAdapter(this),
                    new PortStateListenerAdapter(this));
                PublishLog("Runtime", $"Serial outstation opened on {settings.SerialPort}");
            }

            if (_outstation is null)
            {
                throw new InvalidOperationException("Failed to create DNP3 outstation.");
            }

            _outstation.Transaction(db =>
            {
                foreach (var signal in signalList)
                {
                    AddPoint(db, signal);
                }

                db.DefineStringAttr(0, false, AttributeVariations.DeviceManufacturersName, "DNPTester");
                db.DefineStringAttr(0, true, AttributeVariations.UserAssignedLocation, "Interoperability Test Bench");
            });

            foreach (var signal in signalList)
            {
                PublishSignalValue(signal, ShouldCreateStartupEvent(signal));
            }

            _outstation.Enable();
            _isRunning = true;
            PublishState("Running");
        }
    }

    public void Stop()
    {
        Outstation? outstation;
        OutstationServer? server;
        Runtime? runtime;
        List<CancellationTokenSource> pendingFeedback;

        lock (_sync)
        {
            outstation = _outstation;
            server = _server;
            runtime = _runtime;
            pendingFeedback = _pendingCommandFeedback.ToList();

            _server = null;
            _outstation = null;
            _runtime = null;
            _runtimeSignals.Clear();
            _pendingCommandFeedback.Clear();
            _isRunning = false;
        }

        foreach (var pending in pendingFeedback)
        {
            pending.Cancel();
            pending.Dispose();
        }

        if (outstation is not null)
        {
            try
            {
                outstation.Disable();
            }
            catch
            {
            }
        }

        try
        {
            server?.Shutdown();
        }
        catch
        {
        }

        try
        {
            runtime?.Shutdown();
        }
        catch
        {
        }

        PublishState("Stopped");
    }

    public void PublishSignalValue(Dnp3SimulatorSignal signal, bool forceEvent = false)
    {
        lock (_sync)
        {
            if (_outstation is null)
            {
                return;
            }

            if (!_runtimeSignals.TryGetValue((signal.PointType, signal.Index), out var runtimeSignal))
            {
                runtimeSignal = signal;
                _runtimeSignals[(signal.PointType, signal.Index)] = runtimeSignal;
            }

            CopySignalState(signal, runtimeSignal);
            _outstation.Transaction(db => UpdatePoint(db, runtimeSignal, forceEvent));
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void IndexSignals(IEnumerable<Dnp3SimulatorSignal> signals)
    {
        _runtimeSignals.Clear();
        foreach (var signal in signals)
        {
            _runtimeSignals[(signal.PointType, signal.Index)] = signal;
        }
    }

    private static void CopySignalState(Dnp3SimulatorSignal source, Dnp3SimulatorSignal target)
    {
        target.Label = source.Label;
        target.PointType = source.PointType;
        target.EventClass = source.EventClass;
        target.IsEnabled = source.IsEnabled;
        target.BoolValue = source.BoolValue;
        target.AnalogValue = source.AnalogValue;
        target.UseTimestamp = source.UseTimestamp;
        target.LastUpdatedLocal = source.LastUpdatedLocal;
        target.EventTimestamp = source.EventTimestamp;
        target.Notes = source.Notes;
        target.LastCommandStatus = source.LastCommandStatus;
        target.LastCommandDetail = source.LastCommandDetail;
        target.BinaryCommand = source.BinaryCommand.Clone();
    }

    private static OutstationConfig GetOutstationConfig(Dnp3SlaveConnectionSettings settings)
    {
        return new OutstationConfig(
            settings.OutstationAddress,
            settings.MasterAddress,
            new EventBufferConfig(100, 20, 40, 10, 10, 40, 20, 0))
            .WithDecodeLevel(DecodeLevel.Nothing().WithApplication(AppDecodeLevel.ObjectValues));
    }

    private static EventClass GetEventClass(Dnp3EventClassModel eventClass)
    {
        return eventClass switch
        {
            Dnp3EventClassModel.Class1 => EventClass.Class1,
            Dnp3EventClassModel.Class2 => EventClass.Class2,
            _ => EventClass.Class3
        };
    }

    private static Timestamp GetTimestamp(Dnp3SimulatorSignal signal)
    {
        if (signal.UseTimestamp &&
            signal.EventTimestamp.Kind == SignalEventTimestampStateKind.Synchronized &&
            signal.EventTimestamp.UnixMilliseconds.HasValue)
        {
            return Timestamp.SynchronizedTimestamp(signal.EventTimestamp.UnixMilliseconds.Value);
        }

        return Timestamp.InvalidTimestamp();
    }

    private static UpdateOptions GetUpdateOptions(bool forceEvent = false)
    {
        return forceEvent
            ? UpdateOptions.DetectEvent().WithEventMode(EventMode.Force)
            : UpdateOptions.DetectEvent();
    }

    private static bool ShouldCreateStartupEvent(Dnp3SimulatorSignal signal)
    {
        return signal.IsEnabled &&
               signal.UseTimestamp &&
               signal.PointType is Dnp3OutstationPointType.BinaryInput
                   or Dnp3OutstationPointType.AnalogInput
                   or Dnp3OutstationPointType.BinaryOutputStatus
                   or Dnp3OutstationPointType.AnalogOutputStatus;
    }

    private static void AddPoint(Database db, Dnp3SimulatorSignal signal)
    {
        var eventClass = GetEventClass(signal.EventClass);
        switch (signal.PointType)
        {
            case Dnp3OutstationPointType.BinaryInput:
                db.AddBinaryInput(
                    signal.Index,
                    eventClass,
                    new BinaryInputConfig(
                        StaticBinaryInputVariation.Group1Var1,
                        EventBinaryInputVariation.Group2Var2));
                break;
            case Dnp3OutstationPointType.AnalogInput:
                db.AddAnalogInput(
                    signal.Index,
                    eventClass,
                    new AnalogInputConfig(
                        StaticAnalogInputVariation.Group30Var1,
                        EventAnalogInputVariation.Group32Var3,
                        0.0));
                break;
            case Dnp3OutstationPointType.BinaryOutputStatus:
                db.AddBinaryOutputStatus(
                    signal.Index,
                    eventClass,
                    new BinaryOutputStatusConfig(
                        StaticBinaryOutputStatusVariation.Group10Var1,
                        EventBinaryOutputStatusVariation.Group11Var2));
                break;
            case Dnp3OutstationPointType.AnalogOutputStatus:
                db.AddAnalogOutputStatus(
                    signal.Index,
                    eventClass,
                    new AnalogOutputStatusConfig(
                        StaticAnalogOutputStatusVariation.Group40Var1,
                        EventAnalogOutputStatusVariation.Group42Var3,
                        0.0));
                break;
        }
    }

    private static void UpdatePoint(Database db, Dnp3SimulatorSignal signal, bool forceEvent = false)
    {
        var flags = new Flags(signal.IsEnabled ? Flag.Online : Flag.CommLost);
        var timestamp = GetTimestamp(signal);
        var options = GetUpdateOptions(forceEvent);

        switch (signal.PointType)
        {
            case Dnp3OutstationPointType.BinaryInput:
                db.UpdateBinaryInput(new BinaryInput(signal.Index, signal.BoolValue, flags, timestamp), options);
                break;
            case Dnp3OutstationPointType.AnalogInput:
                db.UpdateAnalogInput(new AnalogInput(signal.Index, signal.AnalogValue, flags, timestamp), options);
                break;
            case Dnp3OutstationPointType.BinaryOutputStatus:
                db.UpdateBinaryOutputStatus(new BinaryOutputStatus(signal.Index, signal.BoolValue, flags, timestamp), options);
                break;
            case Dnp3OutstationPointType.AnalogOutputStatus:
                db.UpdateAnalogOutputStatus(new AnalogOutputStatus(signal.Index, signal.AnalogValue, flags, timestamp), options);
                break;
        }
    }

    private void PublishState(string state)
    {
        PublishLog("State", state);
        StateChanged?.Invoke(state);
    }

    private void PublishLog(string category, string message)
    {
        _logSink?.Invoke(new RuntimeLogEntry
        {
            TimestampLocal = DateTime.Now,
            Category = category,
            Message = message
        });
    }

    private CommandStatus HandleBinaryCommand(ushort index, bool value, string origin)
    {
        if (!_runtimeSignals.TryGetValue((Dnp3OutstationPointType.BinaryOutputStatus, index), out var signal))
        {
            PublishLog("Command", $"{origin} BOS index={index} rejected: point not configured");
            return CommandStatus.NotSupported;
        }

        var scenario = signal.BinaryCommand ?? new BinaryCommandScenario();
        var now = DateTime.Now;
        signal.LastUpdatedLocal = now;

        if (!scenario.IsEnabled)
        {
            signal.BoolValue = value;
            signal.CaptureEdgeTimestamp(now);
            signal.LastCommandStatus = "Success";
            signal.LastCommandDetail = $"{origin} accepted with immediate matching feedback";
            PublishSignalValue(signal);
            PublishLog("Command", $"{origin} BOS index={index} accepted with immediate matching feedback");
            SignalCommanded?.Invoke(signal.Clone());
            return CommandStatus.Success;
        }

        switch (scenario.Behavior)
        {
            case BinaryCommandBehavior.Reject:
                signal.LastCommandStatus = "Rejected";
                signal.LastCommandDetail = $"{origin} rejected by simulator scenario";
                PublishLog("Command", $"{origin} BOS index={index} rejected by scenario");
                SignalCommanded?.Invoke(signal.Clone());
                return CommandStatus.Blocked;

            case BinaryCommandBehavior.SuccessNoFeedback:
                signal.LastCommandStatus = "Accepted";
                signal.LastCommandDetail = $"{origin} accepted without feedback";
                PublishLog("Command", $"{origin} BOS index={index} accepted without feedback");
                SignalCommanded?.Invoke(signal.Clone());
                return CommandStatus.Success;

            case BinaryCommandBehavior.SuccessDelayedMatch:
                signal.LastCommandStatus = "Accepted";
                signal.LastCommandDetail = $"{origin} accepted, delayed matching feedback pending";
                PublishLog("Command", $"{origin} BOS index={index} accepted, delayed matching feedback scheduled");
                SignalCommanded?.Invoke(signal.Clone());
                ScheduleBinaryFeedback(signal, scenario, value, origin, mismatch: false);
                return CommandStatus.Success;

            case BinaryCommandBehavior.SuccessMismatch:
                signal.LastCommandStatus = "Accepted";
                signal.LastCommandDetail = $"{origin} accepted, mismatched feedback pending";
                PublishLog("Command", $"{origin} BOS index={index} accepted, mismatched feedback scheduled");
                SignalCommanded?.Invoke(signal.Clone());
                ScheduleBinaryFeedback(signal, scenario, value, origin, mismatch: true);
                return CommandStatus.Success;

            default:
                signal.BoolValue = value;
                signal.CaptureEdgeTimestamp(now);
                signal.LastCommandStatus = "Success";
                signal.LastCommandDetail = $"{origin} accepted with matching feedback";
                PublishSignalValue(signal);
                PublishLog("Command", $"{origin} BOS index={index} accepted with matching feedback");
                SignalCommanded?.Invoke(signal.Clone());
                return CommandStatus.Success;
        }
    }

    private void ScheduleBinaryFeedback(Dnp3SimulatorSignal commandSignal, BinaryCommandScenario scenario, bool commandedValue, string origin, bool mismatch)
    {
        var cts = new CancellationTokenSource();
        lock (_sync)
        {
            _pendingCommandFeedback.Add(cts);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, scenario.FeedbackDelayMs)), cts.Token);

                Dnp3SimulatorSignal? feedbackSignal;
                lock (_sync)
                {
                    _runtimeSignals.TryGetValue((scenario.FeedbackPointType, scenario.FeedbackIndex), out feedbackSignal);
                }

                if (feedbackSignal is null)
                {
                    PublishLog("Command", $"{origin} feedback skipped: {scenario.FeedbackPointType} index={scenario.FeedbackIndex} not configured");
                    return;
                }

                var feedbackValue = mismatch ? !commandedValue : commandedValue;
                feedbackSignal.BoolValue = feedbackValue;
                feedbackSignal.CaptureEdgeTimestamp(DateTime.Now);
                feedbackSignal.LastCommandStatus = mismatch ? "Mismatch Feedback" : "Feedback Applied";
                feedbackSignal.LastCommandDetail = mismatch
                    ? $"{origin} feedback forced to {(feedbackValue ? "ON" : "OFF")} (mismatch)"
                    : $"{origin} feedback applied to {(feedbackValue ? "ON" : "OFF")}";
                PublishSignalValue(feedbackSignal);
                PublishLog("Command", $"{origin} feedback {feedbackSignal.PointType} index={feedbackSignal.Index} -> {(feedbackValue ? "ON" : "OFF")}");
                SignalCommanded?.Invoke(feedbackSignal.Clone());
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_sync)
                {
                    _pendingCommandFeedback.Remove(cts);
                }

                cts.Dispose();
            }
        });
    }

    private void HandleAnalogCommand(ushort index, double value, string origin)
    {
        if (!_runtimeSignals.TryGetValue((Dnp3OutstationPointType.AnalogOutputStatus, index), out var signal))
        {
            PublishLog("Command", $"{origin} AOS index={index} rejected: point not configured");
            return;
        }

        signal.AnalogValue = value;
        signal.CaptureEdgeTimestamp(DateTime.Now);
        signal.LastCommandStatus = "Success";
        signal.LastCommandDetail = $"{origin} -> {value:0.###}";
        PublishSignalValue(signal);
        PublishLog("Command", $"{origin} AOS index={index} -> {value:0.###}");
        SignalCommanded?.Invoke(signal.Clone());
    }

    private sealed class RuntimeLogger : ILogger
    {
        private readonly Dnp3OutstationService _owner;

        public RuntimeLogger(Dnp3OutstationService owner)
        {
            _owner = owner;
        }

        public void OnMessage(LogLevel level, string message)
        {
            _owner.PublishLog("Trace", $"{level}: {message.Trim()}");
        }
    }

    private sealed class OutstationApplicationAdapter : IOutstationApplication
    {
        private readonly Dnp3OutstationService _owner;

        public OutstationApplicationAdapter(Dnp3OutstationService owner)
        {
            _owner = owner;
        }

        public ushort GetProcessingDelayMs() => 0;
        public WriteTimeResult WriteAbsoluteTime(ulong time) => WriteTimeResult.NotSupported;
        public ApplicationIin GetApplicationIin() => new();
        public RestartDelay ColdRestart() => RestartDelay.Seconds(5);
        public RestartDelay WarmRestart() => RestartDelay.NotSupported();
        FreezeResult IOutstationApplication.FreezeCountersAll(FreezeType freezeType, DatabaseHandle database) => FreezeResult.NotSupported;
        FreezeResult IOutstationApplication.FreezeCountersRange(ushort start, ushort stop, FreezeType freezeType, DatabaseHandle database) => FreezeResult.NotSupported;
        FreezeResult IOutstationApplication.FreezeCountersAllAtTime(DatabaseHandle databaseHandle, ulong time, uint interval) => FreezeResult.NotSupported;
        FreezeResult IOutstationApplication.FreezeCountersRangeAtTime(ushort start, ushort stop, DatabaseHandle databaseHandle, ulong time, uint interval) => FreezeResult.NotSupported;
        bool IOutstationApplication.SupportWriteAnalogDeadBands() => false;
        void IOutstationApplication.BeginWriteAnalogDeadBands() { }
        void IOutstationApplication.WriteAnalogDeadBand(ushort index, double deadBand) { }
        void IOutstationApplication.EndWriteAnalogDeadBands() { }
        bool IOutstationApplication.WriteStringAttr(byte set, byte variation, StringAttr attrType, string value) => false;
        bool IOutstationApplication.WriteFloatAttr(byte set, byte variation, FloatAttr attrType, float value) => false;
        bool IOutstationApplication.WriteDoubleAttr(byte set, byte variation, FloatAttr attrType, double value) => false;
        bool IOutstationApplication.WriteUintAttr(byte set, byte variation, UintAttr attrType, uint value) => false;
        bool IOutstationApplication.WriteIntAttr(byte set, byte variation, IntAttr attrType, int value) => false;
        bool IOutstationApplication.WriteOctetStringAttr(byte set, byte variation, OctetStringAttr attrType, ICollection<byte> value) => false;
        bool IOutstationApplication.WriteBitStringAttr(byte set, byte variation, BitStringAttr attrType, ICollection<byte> value) => false;
        bool IOutstationApplication.WriteTimeAttr(byte set, byte variation, TimeAttr attrType, ulong value) => false;
        void IOutstationApplication.BeginConfirm() => _owner.PublishLog("Protocol", "Master confirmation started");
        void IOutstationApplication.EventCleared(ulong id) => _owner.PublishLog("Protocol", $"Event cleared id={id}");
        void IOutstationApplication.EndConfirm(BufferState state) => _owner.PublishLog("Protocol", $"Confirm ended state={state}");
    }

    private sealed class OutstationInformationAdapter : IOutstationInformation
    {
        private readonly Dnp3OutstationService _owner;

        public OutstationInformationAdapter(Dnp3OutstationService owner)
        {
            _owner = owner;
        }

        public void ProcessRequestFromIdle(RequestHeader header) => _owner.PublishLog("Protocol", "Request received while idle");
        public void BroadcastReceived(FunctionCode functionCode, BroadcastAction action) => _owner.PublishLog("Protocol", $"Broadcast {functionCode} action={action}");
        public void EnterSolicitedConfirmWait(byte ecsn) => _owner.PublishLog("Protocol", $"Solicited confirm wait ecsn={ecsn}");
        public void SolicitedConfirmTimeout(byte ecsn) => _owner.PublishLog("Protocol", $"Solicited confirm timeout ecsn={ecsn}");
        public void SolicitedConfirmReceived(byte ecsn) => _owner.PublishLog("Protocol", $"Solicited confirm received ecsn={ecsn}");
        public void SolicitedConfirmWaitNewRequest() => _owner.PublishLog("Protocol", "Solicited confirm interrupted by new request");
        public void WrongSolicitedConfirmSeq(byte ecsn, byte seq) => _owner.PublishLog("Protocol", $"Wrong solicited confirm seq expected={ecsn} actual={seq}");
        public void UnexpectedConfirm(bool unsolicited, byte seq) => _owner.PublishLog("Protocol", $"Unexpected confirm unsolicited={unsolicited} seq={seq}");
        public void EnterUnsolicitedConfirmWait(byte ecsn) => _owner.PublishLog("Protocol", $"Unsolicited confirm wait ecsn={ecsn}");
        public void UnsolicitedConfirmTimeout(byte ecsn, bool retry) => _owner.PublishLog("Protocol", $"Unsolicited confirm timeout ecsn={ecsn} retry={retry}");
        public void UnsolicitedConfirmed(byte ecsn) => _owner.PublishLog("Protocol", $"Unsolicited confirm received ecsn={ecsn}");
        public void ClearRestartIin() => _owner.PublishLog("Protocol", "Restart IIN cleared");
    }

    private sealed class ControlHandlerAdapter : IControlHandler
    {
        private readonly Dnp3OutstationService _owner;

        public ControlHandlerAdapter(Dnp3OutstationService owner)
        {
            _owner = owner;
        }

        public void BeginFragment() => _owner.PublishLog("Command", "Begin fragment");
        public void EndFragment(DatabaseHandle database) => _owner.PublishLog("Command", "End fragment");
        public CommandStatus SelectG12v1(Group12Var1 control, ushort index, DatabaseHandle database)
        {
            if (!_owner._runtimeSignals.TryGetValue((Dnp3OutstationPointType.BinaryOutputStatus, index), out var signal))
            {
                return CommandStatus.NotSupported;
            }

            if (signal.BinaryCommand.IsEnabled && signal.BinaryCommand.Behavior == BinaryCommandBehavior.Reject)
            {
                _owner.PublishLog("Command", $"Select BOS index={index} rejected by scenario");
                return CommandStatus.Blocked;
            }

            return CommandStatus.Success;
        }

        public CommandStatus OperateG12v1(Group12Var1 control, ushort index, OperateType opType, DatabaseHandle database)
        {
            var turnOn = control.Code.OpType == OpType.LatchOn || control.Code.OpType == OpType.PulseOn;
            return _owner.HandleBinaryCommand(index, turnOn, $"Operate {opType}");
        }

        public CommandStatus SelectG41v1(int value, ushort index, DatabaseHandle database) => CommandStatus.Success;
        public CommandStatus OperateG41v1(int value, ushort index, OperateType opType, DatabaseHandle database)
        {
            _owner.HandleAnalogCommand(index, value, $"Operate {opType}");
            return CommandStatus.Success;
        }

        public CommandStatus SelectG41v2(short value, ushort index, DatabaseHandle database) => CommandStatus.Success;
        public CommandStatus OperateG41v2(short value, ushort index, OperateType opType, DatabaseHandle database)
        {
            _owner.HandleAnalogCommand(index, value, $"Operate {opType}");
            return CommandStatus.Success;
        }

        public CommandStatus SelectG41v3(float value, ushort index, DatabaseHandle database) => CommandStatus.Success;
        public CommandStatus OperateG41v3(float value, ushort index, OperateType opType, DatabaseHandle database)
        {
            _owner.HandleAnalogCommand(index, value, $"Operate {opType}");
            return CommandStatus.Success;
        }

        public CommandStatus SelectG41v4(double value, ushort index, DatabaseHandle database) => CommandStatus.Success;
        public CommandStatus OperateG41v4(double value, ushort index, OperateType opType, DatabaseHandle database)
        {
            _owner.HandleAnalogCommand(index, value, $"Operate {opType}");
            return CommandStatus.Success;
        }
    }

    private sealed class ConnectionStateListenerAdapter : IConnectionStateListener
    {
        private readonly Dnp3OutstationService _owner;

        public ConnectionStateListenerAdapter(Dnp3OutstationService owner)
        {
            _owner = owner;
        }

        public void OnChange(ConnectionState state)
        {
            _owner.PublishState($"TCP {state}");
        }
    }

    private sealed class PortStateListenerAdapter : IPortStateListener
    {
        private readonly Dnp3OutstationService _owner;

        public PortStateListenerAdapter(Dnp3OutstationService owner)
        {
            _owner = owner;
        }

        public void OnChange(PortState state)
        {
            _owner.PublishState($"Serial {state}");
        }
    }
}
