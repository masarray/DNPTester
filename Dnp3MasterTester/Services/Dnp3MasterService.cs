using System.Collections.Concurrent;
using Dnp3MasterTester.Models;
using dnp3;

namespace Dnp3MasterTester.Services;

public sealed class Dnp3MasterService : IDnp3MasterService
{
    private readonly object _sync = new();
    private readonly object _fragmentSync = new();
    private readonly object _commandSync = new();
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
    private ConnectionSettings? _activeSettings;
    private CommandTransactionState? _latestCommandTransaction;
    private CancellationTokenSource? _commandFeedbackTimeoutCts;
    private int _commandSequence;

    public event EventHandler<ConnectionStatusSnapshot>? ConnectionStateChanged;
    public event EventHandler<CommandTransaction>? CommandTransactionUpdated;
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
                _activeSettings = settings;
                _runtime = new Runtime(new RuntimeConfig { NumCoreThreads = 4 });
                _channel = settings.Transport switch
                {
                    DnpTransportType.Serial => MasterChannel.CreateSerialChannel(
                        _runtime,
                        GetMasterChannelConfig(settings),
                        settings.SerialPort,
                        GetSerialSettings(settings),
                        settings.GetSerialOpenRetryDelay(),
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
                    _activeSettings = null;
                    IsConnected = false;
                    CancelCommandFeedbackTimeout();
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

    public async Task ExecuteBinaryControlAsync(ushort index, CommandMode mode, OpType operation, DateTime preparedAtLocal)
    {
        MasterChannel? channel;
        AssociationId? association;
        CommandTransaction transaction;

        lock (_sync)
        {
            channel = _channel;
            association = _association;
        }

        if (channel is null || association is null)
        {
            throw new InvalidOperationException("DNP3 master is not connected.");
        }

        var commandText = $"{mode} {operation} index={index}";
        transaction = StartCommandTransaction(index, mode, operation, preparedAtLocal);

        PublishScadaEvent(
            "Command Requested",
            mode.ToString(),
            "Binary Output",
            index,
            operation.ToString(),
            string.Empty,
            "Pending",
            "-",
            SourceReason.CommandResponse,
            $"Binary control requested: {commandText}");
        WriteTrace("TX", "Command", $"Issuing binary control {commandText}");
        AppendCommandLifecycle(
            transaction.TransactionId,
            "Command Requested",
            $"Binary control requested on {transaction.PointType} {index} using {mode} / {operation}.",
            update => update with { RequestedAtLocal = DateTime.Now });

        try
        {
            var commands = new CommandSet();
            commands.AddG12V1U16(index, Group12Var1.FromCode(ControlCode.FromOpType(operation)));
            await channel.Operate(association, mode, commands);
            var acceptedAt = DateTime.Now;

            PublishScadaEvent(
                "Command Accepted",
                mode.ToString(),
                "Binary Output",
                index,
                operation.ToString(),
                string.Empty,
                "Accepted",
                "-",
                SourceReason.CommandResponse,
                $"Binary control accepted by master service: {commandText}");
            WriteTrace("TX", "Command", $"Binary control accepted {commandText}");
            AppendCommandLifecycle(
                transaction.TransactionId,
                "Command Accepted",
                $"Master service accepted binary control {commandText}.",
                update => update with
                {
                    AcceptanceAtLocal = acceptedAt,
                    AcceptanceResult = "Accepted",
                    AcceptanceLatencyMs = update.RequestedAtLocal.HasValue ? (int)(acceptedAt - update.RequestedAtLocal.Value).TotalMilliseconds : null
                });
            StartCommandFeedbackTimeout(transaction.TransactionId);
        }
        catch (Exception ex)
        {
            PublishScadaEvent(
                "Command Failed",
                mode.ToString(),
                "Binary Output",
                index,
                operation.ToString(),
                string.Empty,
                "Failed",
                "-",
                SourceReason.CommandResponse,
                $"Binary control failed: {commandText}. {ex.Message}");
            WriteTrace("TX", "Command", $"Binary control failed {commandText}: {ex.Message}");
            CancelCommandFeedbackTimeout();
            AppendCommandLifecycle(
                transaction.TransactionId,
                "Command Failed",
                $"Binary control failed: {ex.Message}",
                update => update with
                {
                    AcceptanceAtLocal = DateTime.Now,
                    AcceptanceResult = "Failed",
                    FinalVerdict = "Rejected",
                    IsTerminal = true
                });
            throw;
        }
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

    private static SerialSettings GetSerialSettings(ConnectionSettings settings)
    {
        return new SerialSettings()
            .WithBaudRate(settings.SerialBaudRate)
            .WithDataBits(settings.SerialDataBits)
            .WithStopBits(settings.SerialStopBits)
            .WithParity(settings.SerialParity)
            .WithFlowControl(settings.SerialFlowControl);
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
            RawValue = value,
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
            RawValue = value,
            RawPreviousValue = previous?.Value ?? string.Empty,
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

        TryCorrelateCommandFeedback(pointType, index, value, status, sourceTimestamp);
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
            RawValue = value,
            RawPreviousValue = previousValue,
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

    private CommandTransaction StartCommandTransaction(ushort index, CommandMode mode, OpType operation, DateTime preparedAtLocal)
    {
        CancelCommandFeedbackTimeout();

        CommandTransactionState state;
        lock (_commandSync)
        {
            _commandSequence++;
            state = new CommandTransactionState(
                $"CMD-{_commandSequence:D5}",
                "Binary Output",
                index,
                mode.ToString(),
                operation.ToString(),
                preparedAtLocal,
                null,
                null,
                null,
                "Pending",
                "Pending",
                "In Progress",
                false,
                CommandFeedbackEvidenceKind.None,
                null,
                null,
                false,
                Array.Empty<CommandLifecycleEntry>());

            _latestCommandTransaction = state;
        }

        AppendCommandLifecycle(
            state.TransactionId,
            "Command Prepared",
            $"Operator prepared binary control for index {index}: {mode} / {operation}.",
            update => update);

        return ToCommandTransaction(state);
    }

    private void AppendCommandLifecycle(string transactionId, string stage, string detail, Func<CommandTransactionState, CommandTransactionState> update)
    {
        CommandTransaction? snapshot = null;

        lock (_commandSync)
        {
            if (_latestCommandTransaction is null || _latestCommandTransaction.TransactionId != transactionId)
            {
                return;
            }

            var lifecycle = _latestCommandTransaction.Lifecycle;
            if (!lifecycle.Any(x => x.Stage == stage && x.Detail == detail))
            {
                lifecycle = lifecycle
                    .Concat(new[]
                    {
                        new CommandLifecycleEntry
                        {
                            TimestampLocal = DateTime.Now,
                            Stage = stage,
                            Detail = detail
                        }
                    })
                    .ToArray();
            }

            _latestCommandTransaction = update(_latestCommandTransaction) with { Lifecycle = lifecycle };
            snapshot = ToCommandTransaction(_latestCommandTransaction);
        }

        if (snapshot is not null)
        {
            CommandTransactionUpdated?.Invoke(this, snapshot);
        }
    }

    private void StartCommandFeedbackTimeout(string transactionId)
    {
        var timeoutSeconds = Math.Max(1, _activeSettings?.RequestTimeoutSeconds ?? 5);
        var cts = new CancellationTokenSource();

        lock (_commandSync)
        {
            _commandFeedbackTimeoutCts?.Cancel();
            _commandFeedbackTimeoutCts?.Dispose();
            _commandFeedbackTimeoutCts = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cts.Token);
                AppendCommandLifecycle(
                    transactionId,
                    "Feedback Timeout",
                    $"No command feedback was observed within {timeoutSeconds} seconds.",
                    update =>
                    {
                        if (update.IsTerminal || update.FeedbackAtLocal.HasValue || update.FinalVerdict is "Success" or "Rejected" or "Feedback Mismatch")
                        {
                            return update;
                        }

                        return update with
                        {
                            FeedbackResult = "Timeout",
                            FinalVerdict = "Accepted but no feedback",
                            IsTerminal = true
                        };
                    });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void CancelCommandFeedbackTimeout()
    {
        lock (_commandSync)
        {
            _commandFeedbackTimeoutCts?.Cancel();
            _commandFeedbackTimeoutCts?.Dispose();
            _commandFeedbackTimeoutCts = null;
        }
    }

    private void TryCorrelateCommandFeedback(string pointType, ushort index, string value, string status, SourceTimestampInfo sourceTimestamp)
    {
        CommandTransactionState? current;
        ValueViewerRow? latestValue;
        lock (_commandSync)
        {
            current = _latestCommandTransaction;
        }

        _latestValues.TryGetValue($"{pointType}:{index}", out latestValue);

        if (current is null || current.PointIndex != index || current.IsTerminal || current.FeedbackAtLocal.HasValue)
        {
            return;
        }

        if (pointType is not "Binary Output Status" && !pointType.Contains("Binary Command Event", StringComparison.Ordinal))
        {
            return;
        }

        if (current.RequestedAtLocal is null)
        {
            return;
        }

        if (sourceTimestamp.LocalTime.HasValue && sourceTimestamp.LocalTime.Value < current.RequestedAtLocal.Value)
        {
            return;
        }

        var allowedWindow = TimeSpan.FromSeconds(Math.Max(1, _activeSettings?.RequestTimeoutSeconds ?? 5) + 2);
        var observedAt = sourceTimestamp.LocalTime ?? DateTime.Now;
        if (observedAt - current.RequestedAtLocal.Value > allowedWindow)
        {
            return;
        }

        var expectedValue = GetExpectedBinaryValue(current.Operation);
        var isCommandEvent = pointType.Contains("Binary Command Event", StringComparison.Ordinal);
        var evidenceKind = ResolveFeedbackEvidenceKind(isCommandEvent, pointType, latestValue, value);
        var matched = isCommandEvent
            ? string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
            : string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase);
        var feedbackResult = isCommandEvent
            ? $"Command Event: {status}"
            : $"Status Feedback: {value}";
        var verdict = matched ? "Success" : "Feedback Mismatch";

        CancelCommandFeedbackTimeout();
        AppendCommandLifecycle(
            current.TransactionId,
            matched ? "Feedback Matched" : "Feedback Mismatch",
            isCommandEvent
                ? $"Command event received with status {status}."
                : evidenceKind == CommandFeedbackEvidenceKind.StatusChange
                    ? $"Binary output status changed to {value}, expected {expectedValue}."
                    : $"Binary output status read as {value}, expected {expectedValue} (simple rule).",
            update => update with
            {
                FeedbackAtLocal = observedAt,
                FeedbackResult = feedbackResult,
                FeedbackMatched = matched,
                FeedbackEvidenceKind = evidenceKind,
                FeedbackLatencyMs = update.RequestedAtLocal.HasValue ? (int)(observedAt - update.RequestedAtLocal.Value).TotalMilliseconds : null,
                FinalVerdict = verdict,
                IsTerminal = true
            });

        AppendCommandLifecycle(
            current.TransactionId,
            "Transaction Completed",
            $"Command transaction completed with verdict: {verdict}.",
            update => update);
    }

    private static string GetExpectedBinaryValue(string operation)
    {
        return operation switch
        {
            nameof(OpType.LatchOn) or nameof(OpType.PulseOn) => bool.TrueString,
            nameof(OpType.LatchOff) or nameof(OpType.PulseOff) => bool.FalseString,
            _ => string.Empty
        };
    }

    private static CommandFeedbackEvidenceKind ResolveFeedbackEvidenceKind(bool isCommandEvent, string pointType, ValueViewerRow? latestValue, string value)
    {
        if (isCommandEvent)
        {
            return CommandFeedbackEvidenceKind.CommandEvent;
        }

        if (pointType == "Binary Output Status" && latestValue is not null && !string.Equals(latestValue.Value, value, StringComparison.OrdinalIgnoreCase))
        {
            return CommandFeedbackEvidenceKind.StatusChange;
        }

        return CommandFeedbackEvidenceKind.StatusReadSimpleRule;
    }

    private static CommandTransaction ToCommandTransaction(CommandTransactionState state)
    {
        return new CommandTransaction
        {
            TransactionId = state.TransactionId,
            PointType = state.PointType,
            PointIndex = state.PointIndex,
            CommandMode = state.CommandMode,
            Operation = state.Operation,
            PreparedAtLocal = state.PreparedAtLocal,
            RequestedAtLocal = state.RequestedAtLocal,
            AcceptanceAtLocal = state.AcceptanceAtLocal,
            FeedbackAtLocal = state.FeedbackAtLocal,
            AcceptanceResult = state.AcceptanceResult,
            FeedbackResult = state.FeedbackResult,
            FinalVerdict = state.FinalVerdict,
            FeedbackMatched = state.FeedbackMatched,
            FeedbackEvidenceKind = state.FeedbackEvidenceKind,
            AcceptanceLatencyMs = state.AcceptanceLatencyMs,
            FeedbackLatencyMs = state.FeedbackLatencyMs,
            IsTerminal = state.IsTerminal,
            Lifecycle = state.Lifecycle
        };
    }

    private sealed record CommandTransactionState(
        string TransactionId,
        string PointType,
        ushort PointIndex,
        string CommandMode,
        string Operation,
        DateTime PreparedAtLocal,
        DateTime? RequestedAtLocal,
        DateTime? AcceptanceAtLocal,
        DateTime? FeedbackAtLocal,
        string AcceptanceResult,
        string FeedbackResult,
        string FinalVerdict,
        bool FeedbackMatched,
        CommandFeedbackEvidenceKind FeedbackEvidenceKind,
        int? AcceptanceLatencyMs,
        int? FeedbackLatencyMs,
        bool IsTerminal,
        IReadOnlyList<CommandLifecycleEntry> Lifecycle);

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
