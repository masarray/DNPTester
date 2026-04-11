using System.Collections.Concurrent;
using Dnp3MasterTester.Models;
using dnp3;

namespace Dnp3MasterTester.Services;

public sealed class Dnp3MasterService : IDnp3MasterService
{
    private readonly object _sync = new();
    private readonly object _fragmentSync = new();
    private readonly ConcurrentDictionary<string, ValueViewerRow> _latestValues = new();

    private Runtime? _runtime;
    private MasterChannel? _channel;
    private AssociationId? _association;
    private PollId? _eventPoll;
    private CancellationTokenSource? _sessionCts;
    private Task? _staticRefreshTask;
    private bool _loggingConfigured;
    private FragmentContext _fragmentContext = FragmentContext.Empty;
    private SourceReason _pendingSourceReason = SourceReason.Unknown;

    public event EventHandler<ConnectionStatusSnapshot>? ConnectionStateChanged;
    public event EventHandler<ValueViewerRow>? ValueReceived;
    public event EventHandler<EventLogEntry>? EventLogReceived;
    public event EventHandler<SoeEventRow>? SoeEventReceived;
    public event EventHandler<LinkTraceEntry>? LinkTraceReceived;

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (IsConnected)
                {
                    return;
                }

                ConfigureLoggingOnce();
                var profile = BuildPollingProfile(settings);
                _runtime = new Runtime(new RuntimeConfig { NumCoreThreads = 4 });
                _channel = settings.Transport switch
                {
                    DnpTransportType.Serial => MasterChannel.CreateSerialChannel(
                        _runtime,
                        GetMasterChannelConfig(settings),
                        settings.SerialPort,
                        new SerialSettings(),
                        TimeSpan.FromSeconds(settings.RequestTimeoutSeconds),
                        new PortStateListener(this)),
                    _ => MasterChannel.CreateTcpChannel(
                        _runtime,
                        LinkErrorMode.Close,
                        GetMasterChannelConfig(settings),
                        new EndpointList(settings.Endpoint),
                        new ConnectStrategy(),
                        new ClientStateListener(this))
                };

                _association = _channel.AddAssociation(
                    settings.OutstationAddress,
                    GetAssociationConfig(profile),
                    new ReadHandler(this),
                    new AssociationHandler(),
                    new AssociationInformation(this));

                _eventPoll = _channel.AddPoll(
                    _association,
                    Request.ClassRequest(false, true, true, true),
                    TimeSpan.FromSeconds(profile.FastEventPollSeconds));

                _sessionCts = new CancellationTokenSource();
                _channel.Enable();
                StartStaticRefreshLoop(profile, _channel, _association, _sessionCts.Token);
                IsConnected = true;
            }
        }, cancellationToken);

        RaiseConnection("Connected", "DNP3 master enabled");
        WriteEvent("ENGINE", "Session", "Master channel enabled");
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            lock (_sync)
            {
                try
                {
                    _channel?.Disable();
                    _sessionCts?.Cancel();
                    _channel?.Shutdown();
                    _runtime?.Shutdown();
                }
                finally
                {
                    _association = null;
                    _eventPoll = null;
                    _sessionCts?.Dispose();
                    _sessionCts = null;
                    _staticRefreshTask = null;
                    _channel = null;
                    _runtime = null;
                    IsConnected = false;
                }
            }
        });

        RaiseConnection("Disconnected", "DNP3 master stopped");
        WriteEvent("ENGINE", "Session", "Master channel stopped");
    }

    public Task DemandEventPollAsync()
    {
        PollId? poll;
        MasterChannel? channel;

        lock (_sync)
        {
            poll = _eventPoll;
            channel = _channel;
        }

        if (channel is null || poll is null)
        {
            return Task.CompletedTask;
        }

        SetPendingSourceReason(SourceReason.ManualEventPoll);
        channel.DemandPoll(poll);
        WriteEvent("MASTER", "Poll", "Event poll requested");
        return Task.CompletedTask;
    }

    public async Task RunIntegrityPollAsync()
    {
        MasterChannel? channel;
        AssociationId? association;

        lock (_sync)
        {
            channel = _channel;
            association = _association;
        }

        if (channel is null || association is null)
        {
            return;
        }

        SetPendingSourceReason(SourceReason.ManualIntegrity);
        await channel.Read(association, Request.ClassRequest(true, true, true, true));
        WriteEvent("MASTER", "Poll", "Integrity poll completed");
    }

    public async Task CheckLinkStatusAsync()
    {
        MasterChannel? channel;
        AssociationId? association;

        lock (_sync)
        {
            channel = _channel;
            association = _association;
        }

        if (channel is null || association is null)
        {
            return;
        }

        var result = await channel.CheckLinkStatus(association);
        WriteTrace("TX/RX", "Link", $"Check link status result: {result}");
        WriteEvent("MASTER", "Link", $"Check link status result: {result}");
    }

    private void ConfigureLoggingOnce()
    {
        if (_loggingConfigured)
        {
            return;
        }

        Logging.Configure(new LoggingConfig(), new Logger(this));
        _loggingConfigured = true;
    }

    private static MasterChannelConfig GetMasterChannelConfig(ConnectionSettings settings)
    {
        return new MasterChannelConfig(settings.MasterAddress)
            .WithDecodeLevel(DecodeLevel.Nothing().WithApplication(AppDecodeLevel.ObjectValues));
    }

    private static AssociationConfig GetAssociationConfig(PollingProfile profile)
    {
        return new AssociationConfig(
                profile.DisableUnsolicitedClasses,
                profile.EnableUnsolicitedClasses,
                profile.StartupIntegrityClasses,
                profile.AutoEventScanClasses)
            .WithAutoTimeSync(AutoTimeSync.Lan)
            .WithKeepAliveTimeout(profile.KeepAliveTimeout);
    }

    private void RaiseConnection(string state, string detail)
    {
        ConnectionStateChanged?.Invoke(this, new ConnectionStatusSnapshot
        {
            State = state,
            Detail = detail,
            TimestampUtc = DateTime.UtcNow
        });
    }

    private void WriteEvent(string source, string category, string message)
    {
        WriteTrace("EVENT", category, $"{source}: {message}");
    }

    private void WriteTrace(string direction, string level, string summary)
    {
        LinkTraceReceived?.Invoke(this, new LinkTraceEntry
        {
            TimestampLocal = DateTime.Now,
            Direction = direction,
            Level = level,
            Summary = summary
        });
    }

    private void PublishValue(string pointType, ushort index, string value, string flags, SourceTimestampInfo sourceTimestamp, string source, string status = "-", string? qualifier = null)
    {
        var fragment = GetFragmentContext();
        var eventClass = ClassifyEvent(pointType);
        var pointKey = $"{pointType}:{index}";
        _latestValues.TryGetValue(pointKey, out var previous);

        var row = new ValueViewerRow
        {
            PointType = pointType,
            Index = index,
            Value = value,
            Flags = flags,
            Quality = sourceTimestamp.TimeQuality,
            ReceivedAtLocal = DateTime.Now,
            SourceTimestampLocal = sourceTimestamp.LocalTime,
            SourceTimestampKind = sourceTimestamp.Kind,
            Source = source,
            SourceReason = fragment.SourceReason
        };

        _latestValues.AddOrUpdate(pointKey, row, (_, _) => row);
        ValueReceived?.Invoke(this, row);
        SoeEventReceived?.Invoke(this, new SoeEventRow
        {
            ReceivedAtLocal = DateTime.Now,
            SourceTimestampLocal = sourceTimestamp.LocalTime,
            SourceTimestampKind = sourceTimestamp.Kind,
            ReadType = fragment.ReadType,
            EventClass = eventClass,
            PointType = pointType,
            Index = index,
            Value = value,
            PreviousValue = previous?.Value ?? string.Empty,
            Status = status,
            Flags = flags,
            Quality = sourceTimestamp.TimeQuality,
            Variation = source,
            Qualifier = qualifier ?? fragment.Qualifier,
            IsBroadcast = fragment.IsBroadcast,
            SourceReason = fragment.SourceReason,
            Notes = fragment.Notes
        });

        if (IsBinaryStatePoint(pointType) && previous is not null && !string.Equals(previous.Value, value, StringComparison.Ordinal))
        {
            PublishScadaEvent("Binary State Change", source, pointType, index, value, previous.Value, flags, sourceTimestamp.TimeQuality, fragment.SourceReason, $"State changed from {previous.Value} to {value}");
        }

        if (IsBinaryStatePoint(pointType) && previous is null && fragment.SourceReason != SourceReason.StartupIntegrity)
        {
            PublishScadaEvent("Binary State Initialize", source, pointType, index, value, string.Empty, flags, sourceTimestamp.TimeQuality, fragment.SourceReason, "Initial binary state observed");
        }

        if (pointType.Contains("Command Event", StringComparison.Ordinal))
        {
            PublishScadaEvent("Command Event", source, pointType, index, value, previous?.Value ?? string.Empty, status, sourceTimestamp.TimeQuality, SourceReason.CommandResponse, $"Command event recorded with status {status}");
        }
    }

    private void PublishScadaEvent(string eventType, string source, string pointType, ushort index, string value, string previousValue, string status, string quality, SourceReason sourceReason, string detail)
    {
        EventLogReceived?.Invoke(this, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            EventType = eventType,
            Source = source,
            PointType = pointType,
            Index = index,
            Value = value,
            PreviousValue = previousValue,
            Status = status,
            Quality = quality,
            SourceReason = sourceReason,
            Detail = detail
        });
    }

    private static SourceTimestampInfo BuildSourceTimestamp(Timestamp timestamp)
    {
        var timeQuality = timestamp.Quality.ToString();
        return timestamp.Quality switch
        {
            TimeQuality.SynchronizedTime => SourceTimestampInfo.Valid(DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp.Value).LocalDateTime, timeQuality),
            TimeQuality.UnsynchronizedTime => SourceTimestampInfo.Valid(DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp.Value).LocalDateTime, timeQuality),
            TimeQuality.InvalidTime => SourceTimestampInfo.Invalid(timeQuality),
            _ => SourceTimestampInfo.Unknown(timeQuality)
        };
    }

    private static string FlagText(Flags flags) => flags.Value.ToString();

    private static string QualifierText(HeaderInfo info) => info.Qualifier.ToString();

    private static bool IsBinaryStatePoint(string pointType)
    {
        return pointType is "Binary Input" or "Double Bit Binary" or "Binary Output Status";
    }

    private static string ClassifyEvent(string pointType)
    {
        if (pointType.Contains("Command Event", StringComparison.Ordinal))
        {
            return "Command";
        }

        if (IsBinaryStatePoint(pointType))
        {
            return "Binary";
        }

        return "Telemetry";
    }

    private static PollingProfile BuildPollingProfile(ConnectionSettings settings)
    {
        var definition = settings.GetEffectivePollingProfile();
        var profile = new PollingProfile(
            definition.Kind,
            definition.FastEventPollSeconds,
            definition.StaticRefreshSeconds,
            definition.EnableSlowStaticRefresh,
            definition.EnableAutoEventScan,
            definition.EnableUnsolicited,
            definition.EnableStartupIntegrity,
            definition.KeepAliveTimeout);

        var unsol = profile.EnableUnsolicited ? EventClasses.All() : EventClasses.None();
        var startup = profile.EnableStartupIntegrity ? Classes.All() : Classes.None();
        var autoEvent = profile.EnableAutoEventScan ? EventClasses.All() : EventClasses.None();

        return profile with
        {
            DisableUnsolicitedClasses = unsol,
            EnableUnsolicitedClasses = unsol,
            StartupIntegrityClasses = startup,
            AutoEventScanClasses = autoEvent
        };
    }

    private void StartStaticRefreshLoop(PollingProfile profile, MasterChannel channel, AssociationId association, CancellationToken cancellationToken)
    {
        if (!profile.EnableSlowStaticRefresh || profile.StaticRefreshSeconds <= 0)
        {
            return;
        }

        _staticRefreshTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(profile.StaticRefreshSeconds));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    SetPendingSourceReason(SourceReason.PeriodicStaticRefresh);
                    await channel.Read(association, Request.ClassRequest(true, false, false, false));
                    WriteEvent("MASTER", "Poll", "Static refresh completed");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    private void SetPendingSourceReason(SourceReason sourceReason)
    {
        lock (_fragmentSync)
        {
            _pendingSourceReason = sourceReason;
        }
    }

    private SourceReason ConsumePendingSourceReason()
    {
        lock (_fragmentSync)
        {
            var value = _pendingSourceReason;
            _pendingSourceReason = SourceReason.Unknown;
            return value;
        }
    }

    private static SourceReason ResolveSourceReason(string readType, SourceReason pending)
    {
        if (pending != SourceReason.Unknown)
        {
            return pending;
        }

        return readType switch
        {
            "StartupIntegrity" => SourceReason.StartupIntegrity,
            "Unsolicited" => SourceReason.Unsolicited,
            "PeriodicPoll" => SourceReason.PeriodicEventPoll,
            _ when readType.Contains("Auto", StringComparison.OrdinalIgnoreCase) => SourceReason.AutoEventScan,
            _ => SourceReason.Unknown
        };
    }

    private FragmentContext GetFragmentContext()
    {
        lock (_fragmentSync)
        {
            return _fragmentContext;
        }
    }

    private void SetFragmentContext(string readType, bool isBroadcast, string qualifier = "", string notes = "")
    {
        var sourceReason = ResolveSourceReason(readType, ConsumePendingSourceReason());
        lock (_fragmentSync)
        {
            _fragmentContext = new FragmentContext(readType, isBroadcast, qualifier, sourceReason, notes);
        }
    }

    private readonly record struct FragmentContext(string ReadType, bool IsBroadcast, string Qualifier, SourceReason SourceReason, string Notes)
    {
        public static FragmentContext Empty { get; } = new("Unknown", false, string.Empty, SourceReason.Unknown, string.Empty);
    }

    private sealed record PollingProfile(
        PollingProfileKind Kind,
        int FastEventPollSeconds,
        int StaticRefreshSeconds,
        bool EnableSlowStaticRefresh,
        bool EnableAutoEventScan,
        bool EnableUnsolicited,
        bool EnableStartupIntegrity,
        TimeSpan KeepAliveTimeout)
    {
        public EventClasses DisableUnsolicitedClasses { get; init; } = EventClasses.None();
        public EventClasses EnableUnsolicitedClasses { get; init; } = EventClasses.None();
        public Classes StartupIntegrityClasses { get; init; } = Classes.All();
        public EventClasses AutoEventScanClasses { get; init; } = EventClasses.None();
    }

    private sealed class Logger(Dnp3MasterService owner) : ILogger
    {
        public void OnMessage(LogLevel level, string message)
        {
            owner.WriteTrace("TRACE", level.ToString(), message.Trim());
        }
    }

    private sealed class ClientStateListener(Dnp3MasterService owner) : IClientStateListener
    {
        public void OnChange(ClientState state)
        {
            owner.RaiseConnection(state.ToString(), $"Client state changed to {state}");
            owner.WriteEvent("CHANNEL", "ClientState", state.ToString());
        }
    }

    private sealed class PortStateListener(Dnp3MasterService owner) : IPortStateListener
    {
        public void OnChange(PortState state)
        {
            owner.RaiseConnection(state.ToString(), $"Port state changed to {state}");
            owner.WriteEvent("CHANNEL", "PortState", state.ToString());
        }
    }

    private sealed class AssociationHandler : IAssociationHandler
    {
        public UtcTimestamp GetCurrentTime()
        {
            return UtcTimestamp.Valid((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }

    private sealed class AssociationInformation(Dnp3MasterService owner) : IAssociationInformation
    {
        public void TaskStart(TaskType taskType, FunctionCode fc, byte seq)
        {
            owner.WriteEvent("ASSOC", "TaskStart", $"{taskType} / {fc} seq={seq}");
        }

        public void TaskSuccess(TaskType taskType, FunctionCode fc, byte seq)
        {
            owner.WriteEvent("ASSOC", "TaskSuccess", $"{taskType} / {fc} seq={seq}");
        }

        public void TaskFail(TaskType taskType, TaskError error)
        {
            owner.WriteEvent("ASSOC", "TaskFail", $"{taskType} failed: {error}");
        }

        public void UnsolicitedResponse(bool isDuplicate, byte seq)
        {
            owner.WriteEvent("ASSOC", "Unsolicited", $"duplicate={isDuplicate} seq={seq}");
        }
    }

    private sealed class ReadHandler(Dnp3MasterService owner) : IReadHandler
    {
        public void BeginFragment(ReadType readType, ResponseHeader header)
        {
            owner.SetFragmentContext(readType.ToString(), header.Iin.Iin1.Broadcast, notes: $"Fragment start IIN={header.Iin}");
            owner.WriteEvent("READ", "BeginFragment", $"{readType} broadcast={header.Iin.Iin1.Broadcast}");
        }

        public void EndFragment(ReadType readType, ResponseHeader header)
        {
            owner.SetFragmentContext(readType.ToString(), header.Iin.Iin1.Broadcast, notes: "Fragment completed");
            owner.WriteEvent("READ", "EndFragment", readType.ToString());
        }

        public void HandleBinaryInput(HeaderInfo info, ICollection<BinaryInput> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Binary Input", value.Index, value.Value.ToString(), FlagText(value.Flags), BuildSourceTimestamp(value.Time), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleDoubleBitBinaryInput(HeaderInfo info, ICollection<DoubleBitBinaryInput> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Double Bit Binary", value.Index, value.Value.ToString(), FlagText(value.Flags), BuildSourceTimestamp(value.Time), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleBinaryOutputStatus(HeaderInfo info, ICollection<BinaryOutputStatus> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Binary Output Status", value.Index, value.Value.ToString(), FlagText(value.Flags), BuildSourceTimestamp(value.Time), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleCounter(HeaderInfo info, ICollection<Counter> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Counter", value.Index, value.Value.ToString(), FlagText(value.Flags), BuildSourceTimestamp(value.Time), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleFrozenCounter(HeaderInfo info, ICollection<FrozenCounter> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Frozen Counter", value.Index, value.Value.ToString(), FlagText(value.Flags), BuildSourceTimestamp(value.Time), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleAnalogInput(HeaderInfo info, ICollection<AnalogInput> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Analog Input", value.Index, value.Value.ToString("G"), FlagText(value.Flags), BuildSourceTimestamp(value.Time), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleFrozenAnalogInput(HeaderInfo info, ICollection<FrozenAnalogInput> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Frozen Analog Input", value.Index, value.Value.ToString("G"), FlagText(value.Flags), BuildSourceTimestamp(value.Time), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleAnalogOutputStatus(HeaderInfo info, ICollection<AnalogOutputStatus> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Analog Output Status", value.Index, value.Value.ToString("G"), FlagText(value.Flags), BuildSourceTimestamp(value.Time), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleBinaryOutputCommandEvent(HeaderInfo info, ICollection<BinaryOutputCommandEvent> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Binary Command Event", value.Index, value.CommandedState.ToString(), value.Status.ToString(), BuildSourceTimestamp(value.Time), info.Variation.ToString(), value.Status.ToString(), QualifierText(info));
            }
        }

        public void HandleAnalogOutputCommandEvent(HeaderInfo info, ICollection<AnalogOutputCommandEvent> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Analog Command Event", value.Index, value.CommandedValue.ToString("G"), value.Status.ToString(), BuildSourceTimestamp(value.Time), info.Variation.ToString(), value.Status.ToString(), QualifierText(info));
            }
        }

        public void HandleUnsignedInteger(HeaderInfo info, ICollection<UnsignedInteger> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Unsigned Integer", value.Index, value.Value.ToString(), "-", SourceTimestampInfo.NotSupplied("-"), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleOctetString(HeaderInfo info, ICollection<OctetString> values)
        {
            foreach (var value in values)
            {
                owner.PublishValue("Octet String", value.Index, BitConverter.ToString(value.Value.ToArray()), "-", SourceTimestampInfo.NotSupplied("-"), info.Variation.ToString(), qualifier: QualifierText(info));
            }
        }

        public void HandleStringAttr(HeaderInfo info, StringAttr attr, byte set, byte var, string value) { }
        public void HandleUintAttr(HeaderInfo info, UintAttr attr, byte set, byte var, uint value) { }
        public void HandleBoolAttr(HeaderInfo info, BoolAttr attr, byte set, byte var, bool value) { }
        public void HandleIntAttr(HeaderInfo info, IntAttr attr, byte set, byte var, int value) { }
        public void HandleTimeAttr(HeaderInfo info, TimeAttr attr, byte set, byte var, ulong value) { }
        public void HandleFloatAttr(HeaderInfo info, FloatAttr attr, byte set, byte var, double value) { }
        public void HandleVariationListAttr(HeaderInfo info, VariationListAttr attr, byte set, byte var, ICollection<AttrItem> value) { }
        public void HandleOctetStringAttr(HeaderInfo info, OctetStringAttr attr, byte set, byte var, ICollection<byte> value) { }
        public void HandleBitStringAttr(HeaderInfo info, BitStringAttr attr, byte set, byte var, ICollection<byte> value) { }
        public void HandleAbsTime(HeaderInfo info, Timestamp time) { }
    }
}
