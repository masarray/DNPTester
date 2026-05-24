using System.Collections.Concurrent;
using Dnp3MasterTester.Models;
using Dnp3MasterTester.Protocol;

namespace Dnp3MasterTester.Services;

public sealed class Dnp3MasterService : IDnp3MasterService
{
    private readonly object _sync = new();
    private readonly object _commandSync = new();
    private readonly ConcurrentDictionary<string, ValueViewerRow> _latestValues = new();
    private Dnp3TransportSession? _session;
    private CancellationTokenSource? _sessionCts;
    private Task? _eventPollTask;
    private Task? _staticRefreshTask;
    private ConnectionSettings? _activeSettings;
    private CommandTransactionState? _latestCommandTransaction;
    private CancellationTokenSource? _commandFeedbackTimeoutCts;
    private int _commandSequence;
    private bool _hasDeviceResponse;

    public event EventHandler<ConnectionStatusSnapshot>? ConnectionStateChanged;
    public event EventHandler<CommandTransaction>? CommandTransactionUpdated;
    public event EventHandler<ValueViewerRow>? ValueReceived;
    public event EventHandler<EventLogEntry>? EventLogReceived;
    public event EventHandler<SoeEventRow>? SoeEventReceived;
    public event EventHandler<LinkTraceEntry>? LinkTraceReceived;

    public bool IsConnected { get; private set; }

    public bool HasDeviceResponse
    {
        get
        {
            lock (_sync)
            {
                return _hasDeviceResponse;
            }
        }
    }

    public bool IsOperationalReady
    {
        get
        {
            lock (_sync)
            {
                return IsConnected && _hasDeviceResponse;
            }
        }
    }

    public async Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (IsConnected)
            {
                return;
            }
        }

        var profile = BuildPollingProfile(settings);
        var session = new Dnp3TransportSession(settings, WriteTrace);
        await session.OpenAsync(cancellationToken);
        await session.ResetLinkAsync(cancellationToken);

        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _activeSettings = settings;
            _session = session;
            _sessionCts = sessionCts;
            _hasDeviceResponse = false;
            IsConnected = true;
        }

        RaiseConnection("Transport Open", "Native C# DNP3 master transport opened; waiting for outstation response");
        WriteEvent("ENGINE", "Session", "Native C# DNP3 master enabled");
        WriteEvent(
            "MASTER",
            "Polling",
            profile.EnableAutoEventScan
                ? $"Event poll active every {profile.FastEventPollSeconds}s"
                : "Auto event poll disabled");

        if (profile.EnableStartupIntegrity)
        {
            _ = Task.Run(() => RunReadAsync(true, true, true, true, Dnp3ReadReason.StartupIntegrity, sessionCts.Token), sessionCts.Token);
        }

        StartEventPollLoop(profile, sessionCts.Token);
        StartStaticRefreshLoop(profile, sessionCts.Token);
    }

    public async Task DisconnectAsync()
    {
        Dnp3TransportSession? session;
        CancellationTokenSource? cts;
        lock (_sync)
        {
            session = _session;
            cts = _sessionCts;
            _session = null;
            _sessionCts = null;
            _activeSettings = null;
            _hasDeviceResponse = false;
            IsConnected = false;
        }

        cts?.Cancel();
        cts?.Dispose();
        if (session is not null)
        {
            await session.DisposeAsync();
        }

        _eventPollTask = null;
        _staticRefreshTask = null;
        CancelCommandFeedbackTimeout();
        RaiseConnection("Disconnected", "Native C# DNP3 master stopped");
        WriteEvent("ENGINE", "Session", "Native C# DNP3 master stopped");
    }

    public Task DemandEventPollAsync() => RunReadAsync(false, true, true, true, Dnp3ReadReason.EventPoll, CancellationToken.None);

    public Task RunIntegrityPollAsync() => RunReadAsync(true, true, true, true, Dnp3ReadReason.ManualIntegrity, CancellationToken.None);

    public async Task CheckLinkStatusAsync()
    {
        var session = GetConnectedSession();
        using var cts = CreateRequestCts();
        await session.CheckLinkStatusAsync(cts.Token);
        MarkDeviceResponding("DNP3 link status response received");
        WriteEvent("MASTER", "Link", "Link status response received");
    }

    public async Task ExecuteBinaryControlAsync(
        ushort index,
        CommandMode mode,
        OpType operation,
        DateTime preparedAtLocal,
        string? expectedFeedbackPointType = null,
        ushort? expectedFeedbackIndex = null,
        int? correlationWindowMs = null)
    {
        if (!IsOperationalReady)
        {
            throw new InvalidOperationException("DNP3 outstation has not responded yet. Run Link Status or Integrity Poll before operating a command.");
        }

        var session = GetConnectedSession();
        var transaction = StartCommandTransaction(index, mode, operation, preparedAtLocal, expectedFeedbackPointType, expectedFeedbackIndex, correlationWindowMs);
        var commandText = $"{mode} {operation} index={index}";
        PublishScadaEvent("Command Requested", mode.ToString(), "Binary Output", index, operation.ToString(), string.Empty, "Pending", "-", SourceReason.CommandResponse, $"Binary control requested: {commandText}");
        WriteTrace("TX", "Command", $"Issuing binary control {commandText}");
        AppendCommandLifecycle(transaction.TransactionId, "Command Requested", $"Binary control requested on Binary Output {index} using {mode} / {operation}.", update => update with { RequestedAtLocal = DateTime.Now });

        try
        {
            using var cts = CreateRequestCts();
            var response = await session.OperateBinaryAsync(index, mode, operation, cts.Token);
            PublishResponse(response, Dnp3ReadReason.Command);
            var commandStatus = response.CommandStatuses.LastOrDefault();
            if (commandStatus is not null && !commandStatus.Status.Contains("Success", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Outstation rejected command: {commandStatus.Status}");
            }

            var acceptedAt = DateTime.Now;
            PublishScadaEvent("Command Accepted", mode.ToString(), "Binary Output", index, operation.ToString(), string.Empty, "Accepted", "-", SourceReason.CommandResponse, $"Binary control accepted: {commandText}");
            AppendCommandLifecycle(
                transaction.TransactionId,
                "Command Accepted",
                $"Native master accepted binary control {commandText}.",
                update => update with
                {
                    AcceptanceAtLocal = acceptedAt,
                    AcceptanceResult = commandStatus?.Status ?? "Accepted",
                    AcceptanceLatencyMs = update.RequestedAtLocal.HasValue ? (int)(acceptedAt - update.RequestedAtLocal.Value).TotalMilliseconds : null
                });
            StartCommandFeedbackTimeout(transaction.TransactionId);
        }
        catch (Exception ex)
        {
            PublishScadaEvent("Command Failed", mode.ToString(), "Binary Output", index, operation.ToString(), string.Empty, "Failed", "-", SourceReason.CommandResponse, $"Binary control failed: {commandText}. {ex.Message}");
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

    private async Task RunReadAsync(bool class0, bool class1, bool class2, bool class3, Dnp3ReadReason reason, CancellationToken cancellationToken)
    {
        var session = GetConnectedSession();
        using var timeoutCts = CreateRequestCts(cancellationToken);
        var response = await session.ReadAsync(class0, class1, class2, class3, timeoutCts.Token);
        PublishResponse(response, reason);
        WriteEvent("MASTER", "Poll", $"{reason} read completed");
        MarkDeviceResponding($"DNP3 {reason} response received");
    }

    private void PublishResponse(Dnp3ApplicationResponse response, Dnp3ReadReason reason)
    {
        foreach (var measurement in response.Measurements)
        {
            PublishValue(
                measurement.PointType,
                measurement.Index,
                measurement.Value,
                measurement.Flags,
                measurement.Timestamp,
                measurement.Variation,
                measurement.Status,
                measurement.Qualifier,
                ToSourceReason(reason),
                reason.ToString());
        }

        if (response.InternalIndications != 0)
        {
            WriteTrace("RX", "IIN", $"Internal indications: 0x{response.InternalIndications:X4}");
        }
    }

    private void StartEventPollLoop(PollingProfile profile, CancellationToken cancellationToken)
    {
        if (!profile.EnableAutoEventScan || profile.FastEventPollSeconds <= 0)
        {
            return;
        }

        _eventPollTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(profile.FastEventPollSeconds));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    await RunReadAsync(false, true, true, true, Dnp3ReadReason.EventPoll, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteTrace("RX", "EventPoll", ex.Message);
            }
        }, cancellationToken);
    }

    private void StartStaticRefreshLoop(PollingProfile profile, CancellationToken cancellationToken)
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
                    await RunReadAsync(true, false, false, false, Dnp3ReadReason.StaticRefresh, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteTrace("RX", "StaticRefresh", ex.Message);
            }
        }, cancellationToken);
    }

    private Dnp3TransportSession GetConnectedSession()
    {
        lock (_sync)
        {
            return _session ?? throw new InvalidOperationException("DNP3 master is not connected.");
        }
    }

    private CancellationTokenSource CreateRequestCts(CancellationToken cancellationToken = default)
    {
        var seconds = Math.Max(1, _activeSettings?.RequestTimeoutSeconds ?? 5);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
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

    private void MarkDeviceResponding(string evidence)
    {
        var shouldPublish = false;
        lock (_sync)
        {
            if (!_hasDeviceResponse)
            {
                _hasDeviceResponse = true;
                shouldPublish = true;
            }
        }

        if (shouldPublish)
        {
            RaiseConnection("Device Responding", evidence);
            PublishScadaEvent("Device Response", "DNP3", "Outstation", 0, "Responding", string.Empty, "Confirmed", "-", SourceReason.Unknown, evidence);
        }
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

    private void PublishValue(
        string pointType,
        ushort index,
        string value,
        string flags,
        SourceTimestampInfo sourceTimestamp,
        string source,
        string status,
        string qualifier,
        SourceReason sourceReason,
        string readType)
    {
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
            SourceReason = sourceReason
        };

        _latestValues.AddOrUpdate(pointKey, row, (_, _) => row);
        MarkDeviceResponding($"DNP3 object response received from outstation: {pointType} {index}");
        ValueReceived?.Invoke(this, row);
        SoeEventReceived?.Invoke(this, new SoeEventRow
        {
            ReceivedAtLocal = DateTime.Now,
            SourceTimestampLocal = sourceTimestamp.LocalTime,
            SourceTimestampKind = sourceTimestamp.Kind,
            ReadType = readType,
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
            Qualifier = qualifier,
            SourceReason = sourceReason,
            Notes = "Native C# master decoder"
        });

        if (IsBinaryStatePoint(pointType) && previous is not null && !string.Equals(previous.Value, value, StringComparison.Ordinal))
        {
            PublishScadaEvent("Binary State Change", source, pointType, index, value, previous.Value, flags, sourceTimestamp.TimeQuality, sourceReason, $"State changed from {previous.Value} to {value}", sourceTimestamp);
        }

        if (IsBinaryStatePoint(pointType) && previous is null && sourceReason != SourceReason.StartupIntegrity)
        {
            PublishScadaEvent("Binary State Initialize", source, pointType, index, value, string.Empty, flags, sourceTimestamp.TimeQuality, sourceReason, "Initial binary state observed", sourceTimestamp);
        }

        if (pointType.Contains("Command Event", StringComparison.Ordinal))
        {
            PublishScadaEvent("Command Event", source, pointType, index, value, previous?.Value ?? string.Empty, status, sourceTimestamp.TimeQuality, SourceReason.CommandResponse, $"Command event recorded with status {status}", sourceTimestamp);
        }

        TryCorrelateCommandFeedback(pointType, index, value, status, sourceTimestamp, previous);
    }

    private void PublishScadaEvent(
        string eventType,
        string source,
        string pointType,
        ushort index,
        string value,
        string previousValue,
        string status,
        string quality,
        SourceReason sourceReason,
        string detail,
        SourceTimestampInfo? sourceTimestamp = null)
    {
        EventLogReceived?.Invoke(this, new EventLogEntry
        {
            TimestampLocal = DateTime.Now,
            SourceTimestampLocal = sourceTimestamp?.LocalTime,
            SourceTimestampKind = sourceTimestamp?.Kind ?? SourceTimestampKind.NotSupplied,
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

    private static SourceReason ToSourceReason(Dnp3ReadReason reason) => reason switch
    {
        Dnp3ReadReason.StartupIntegrity => SourceReason.StartupIntegrity,
        Dnp3ReadReason.ManualIntegrity => SourceReason.ManualIntegrity,
        Dnp3ReadReason.EventPoll => SourceReason.PeriodicEventPoll,
        Dnp3ReadReason.StaticRefresh => SourceReason.PeriodicStaticRefresh,
        Dnp3ReadReason.Command => SourceReason.CommandResponse,
        _ => SourceReason.Unknown
    };

    private static bool IsBinaryStatePoint(string pointType) => pointType is "Binary Input" or "Double Bit Binary" or "Binary Output Status";

    private static string ClassifyEvent(string pointType)
    {
        if (pointType.Contains("Command Event", StringComparison.Ordinal))
        {
            return "Command";
        }

        return IsBinaryStatePoint(pointType) ? "Binary" : "Telemetry";
    }

    private static PollingProfile BuildPollingProfile(ConnectionSettings settings)
    {
        var definition = settings.GetEffectivePollingProfile();
        return new PollingProfile(
            definition.Kind,
            definition.FastEventPollSeconds,
            definition.StaticRefreshSeconds,
            definition.EnableSlowStaticRefresh,
            definition.EnableAutoEventScan,
            definition.EnableUnsolicited,
            definition.EnableStartupIntegrity,
            definition.KeepAliveTimeout);
    }

    private CommandTransaction StartCommandTransaction(
        ushort index,
        CommandMode mode,
        OpType operation,
        DateTime preparedAtLocal,
        string? expectedFeedbackPointType,
        ushort? expectedFeedbackIndex,
        int? correlationWindowMs)
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
                expectedFeedbackPointType,
                expectedFeedbackIndex,
                correlationWindowMs,
                false,
                Array.Empty<CommandLifecycleEntry>());

            _latestCommandTransaction = state;
        }

        AppendCommandLifecycle(state.TransactionId, "Command Prepared", $"Operator prepared binary control for index {index}: {mode} / {operation}.", update => update);
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
                lifecycle = lifecycle.Concat(new[]
                {
                    new CommandLifecycleEntry
                    {
                        TimestampLocal = DateTime.Now,
                        Stage = stage,
                        Detail = detail
                    }
                }).ToArray();
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
        CommandTransactionState? current;
        lock (_commandSync)
        {
            current = _latestCommandTransaction;
        }

        var timeoutMs = Math.Max(250, current?.CorrelationWindowMs ?? ((_activeSettings?.RequestTimeoutSeconds ?? 5) * 1000));
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
                await Task.Delay(TimeSpan.FromMilliseconds(timeoutMs), cts.Token);
                AppendCommandLifecycle(
                    transactionId,
                    "Feedback Timeout",
                    $"No command feedback was observed within {timeoutMs} ms.",
                    update => update.IsTerminal || update.FeedbackAtLocal.HasValue
                        ? update
                        : update with
                        {
                            FeedbackResult = "Timeout",
                            FinalVerdict = "Accepted but no feedback",
                            IsTerminal = true
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

    private void TryCorrelateCommandFeedback(
        string pointType,
        ushort index,
        string value,
        string status,
        SourceTimestampInfo sourceTimestamp,
        ValueViewerRow? previousValue)
    {
        CommandTransactionState? current;
        lock (_commandSync)
        {
            current = _latestCommandTransaction;
        }

        if (current is null || current.IsTerminal || current.FeedbackAtLocal.HasValue || current.RequestedAtLocal is null)
        {
            return;
        }

        var hasConfiguredFeedback = !string.IsNullOrWhiteSpace(current.ExpectedFeedbackPointType) && current.ExpectedFeedbackIndex.HasValue;
        var expectedPointType = current.ExpectedFeedbackPointType ?? current.PointType;
        var expectedPointIndex = current.ExpectedFeedbackIndex ?? current.PointIndex;
        var isConfiguredFeedbackPoint = string.Equals(pointType, expectedPointType, StringComparison.OrdinalIgnoreCase) && index == expectedPointIndex;
        var isCommandEventFallback = !hasConfiguredFeedback && pointType.Contains("Command Event", StringComparison.OrdinalIgnoreCase) && index == current.PointIndex;

        if (!isConfiguredFeedbackPoint && !isCommandEventFallback)
        {
            return;
        }

        var receivedAt = DateTime.Now;
        var allowedWindow = TimeSpan.FromMilliseconds(Math.Max(250, current.CorrelationWindowMs ?? ((Math.Max(1, _activeSettings?.RequestTimeoutSeconds ?? 5) + 2) * 1000)));
        if (receivedAt - current.RequestedAtLocal.Value > allowedWindow)
        {
            return;
        }

        var expectedValue = GetExpectedBinaryValue(current.Operation);
        var evidenceKind = pointType.Contains("Command Event", StringComparison.OrdinalIgnoreCase)
            ? CommandFeedbackEvidenceKind.CommandEvent
            : ResolveFeedbackEvidenceKind(pointType, previousValue, value);
        var statusIndicatesFailure = evidenceKind == CommandFeedbackEvidenceKind.CommandEvent && !IsPositiveCommandStatus(status);
        var matched = string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase) && !statusIndicatesFailure;
        var feedbackResult = evidenceKind == CommandFeedbackEvidenceKind.CommandEvent ? $"Command Event: {value} / {status}" : $"Status Feedback: {value}";
        var verdict = matched ? "Success" : "Feedback Mismatch";

        CancelCommandFeedbackTimeout();
        AppendCommandLifecycle(
            current.TransactionId,
            matched ? "Feedback Matched" : "Feedback Mismatch",
            evidenceKind == CommandFeedbackEvidenceKind.CommandEvent
                ? $"Command-event feedback reported {value} with status {status}; expected {expectedValue}."
                : $"Configured feedback point read as {value}, expected {expectedValue}.",
            update => update with
            {
                FeedbackAtLocal = receivedAt,
                FeedbackResult = feedbackResult,
                FeedbackMatched = matched,
                FeedbackEvidenceKind = evidenceKind,
                FeedbackLatencyMs = update.RequestedAtLocal.HasValue ? (int)(receivedAt - update.RequestedAtLocal.Value).TotalMilliseconds : null,
                FinalVerdict = verdict,
                IsTerminal = true
            });
        AppendCommandLifecycle(current.TransactionId, "Transaction Completed", $"Command transaction completed with verdict: {verdict}.", update => update);
    }

    private static bool IsPositiveCommandStatus(string status) => string.IsNullOrWhiteSpace(status) || status == "-" || status.Contains("Success", StringComparison.OrdinalIgnoreCase);

    private static string GetExpectedBinaryValue(string operation) => operation switch
    {
        nameof(OpType.LatchOn) or nameof(OpType.PulseOn) => bool.TrueString,
        nameof(OpType.LatchOff) or nameof(OpType.PulseOff) => bool.FalseString,
        _ => string.Empty
    };

    private static CommandFeedbackEvidenceKind ResolveFeedbackEvidenceKind(string pointType, ValueViewerRow? latestValue, string value)
    {
        if ((pointType == "Binary Output Status" || pointType == "Binary Input") && latestValue is not null && !string.Equals(latestValue.Value, value, StringComparison.OrdinalIgnoreCase))
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
            ExpectedFeedbackPointType = state.ExpectedFeedbackPointType,
            ExpectedFeedbackIndex = state.ExpectedFeedbackIndex,
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
        string? ExpectedFeedbackPointType,
        ushort? ExpectedFeedbackIndex,
        int? CorrelationWindowMs,
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
        TimeSpan KeepAliveTimeout);
}
