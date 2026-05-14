using Dnp3MasterTester.Models;
using Dnp3MasterTester.Models.Reports;

namespace Dnp3MasterTester.Services.Reports;

public static class FatReportSnapshotBuilder
{
    public static FatTestSessionSnapshot Build(
        ReportBrandingSettings branding,
        ConnectionSettings settings,
        string pointCatalogProfileName,
        string connectionState,
        string connectionDetail,
        IReadOnlyCollection<ValueViewerRow> values,
        IReadOnlyCollection<EventLogEntry> events,
        IReadOnlyCollection<SoeEventRow> soeEvents,
        IReadOnlyCollection<LinkTraceEntry> traces,
        CommandTransaction? latestCommand,
        ReportManualAssessment manualAssessment,
        bool isFinalized,
        string? existingReportId = null,
        DateTime? finalizedAtLocal = null)
    {
        var reportId = string.IsNullOrWhiteSpace(existingReportId)
            ? $"FAT-{DateTime.Now:yyyyMMdd-HHmmss}"
            : existingReportId;

        var eventEvidence = events.Take(80).Select(ToEventEvidence).ToArray();
        var soeEvidence = soeEvents.Take(80).Select(ToSoeEvidence).ToArray();
        var traceEvidence = traces.Take(120).Select(ToTraceEvidence).ToArray();
        var pointEvidence = values.Take(120).Select(ToPointEvidence).ToArray();
        var commandEvidence = latestCommand is null
            ? Array.Empty<CommandEvidenceRecord>()
            : new[] { ToCommandEvidence(latestCommand) };
        var fatItems = BuildFatItems(connectionState, values, events, soeEvents, traces, latestCommand, manualAssessment).ToArray();
        var observations = BuildObservations(values, soeEvents, traces, fatItems, latestCommand).ToArray();

        return new FatTestSessionSnapshot
        {
            ReportId = reportId,
            GeneratedAtLocal = DateTime.Now,
            FinalizedAtLocal = finalizedAtLocal,
            IsFinalized = isFinalized,
            Branding = branding,
            ConnectionProfile = $"{settings.Transport} / Master {settings.MasterAddress} / Outstation {settings.OutstationAddress}",
            ConnectionTarget = settings.Transport == DnpTransportType.Serial ? settings.GetSerialSummary() : settings.Endpoint,
            ConnectionState = connectionState,
            ConnectionDetail = connectionDetail,
            PollingProfile = settings.GetEffectivePollingProfile().Summary,
            PointCatalogProfileName = pointCatalogProfileName,
            OverallVerdict = ResolveOverallVerdict(fatItems),
            FatExecutionStatus = ResolveFatExecutionStatus(fatItems),
            TechnicalResult = ResolveTechnicalResult(fatItems),
            FatItems = fatItems,
            EventEvidence = eventEvidence,
            SoeEvidence = soeEvidence,
            TraceEvidence = traceEvidence,
            PointEvidence = pointEvidence,
            CommandEvidence = commandEvidence,
            Observations = observations,
            TraceEvidenceCount = traces.Count
        };
    }

    private static IEnumerable<FatTestItemResult> BuildFatItems(
        string connectionState,
        IReadOnlyCollection<ValueViewerRow> values,
        IReadOnlyCollection<EventLogEntry> events,
        IReadOnlyCollection<SoeEventRow> soeEvents,
        IReadOnlyCollection<LinkTraceEntry> traces,
        CommandTransaction? latestCommand,
        ReportManualAssessment manualAssessment)
    {
        var hasConnectionEvidence = string.Equals(connectionState, "Device Responding", StringComparison.OrdinalIgnoreCase) ||
            values.Count > 0 ||
            soeEvents.Count > 0 ||
            events.Any(x => x.EventType.Equals("Device Response", StringComparison.OrdinalIgnoreCase));
        yield return Item(
            "7.5.1",
            "Communication establishment",
            "Verify that the master can establish DNP3 communication with the DUT.",
            "A valid session is established and protocol evidence is captured.",
            "Connect to the DUT and require at least one DNP3 outstation response, link-status response, integrity response, or decoded object.",
            "Device Responding state, Device Response event, SOE row, or any decoded point value from the outstation.",
            "Marked tested only after real outstation response evidence exists; open socket/port or channel trace alone is not sufficient.",
            hasConnectionEvidence ? ReportVerdict.Pass : ReportVerdict.Inconclusive,
            values.Count + soeEvents.Count + events.Count(x => x.EventType.Equals("Device Response", StringComparison.OrdinalIgnoreCase)),
            hasConnectionEvidence ? "Outstation response evidence is present." : "Transport may be open, but no outstation response evidence has been captured yet.");

        yield return Item(
            "7.5.2",
            "Point read verification",
            "Verify that configured static points can be read and displayed with engineering labels.",
            "Integrity/static read returns mapped point values.",
            "Run Integrity Poll after connection and verify point values in Value Viewer.",
            "At least one value row with point type, index, value, quality, and source context.",
            "Marked tested when Value Viewer contains one or more received point values.",
            values.Count > 0 ? ReportVerdict.Pass : ReportVerdict.NotTested,
            values.Count,
            values.Count > 0 ? $"{values.Count} point values are available." : "Run an integrity poll to collect point evidence.");

        var binaryCount = values.Count(x => x.PointType.Contains("Binary", StringComparison.OrdinalIgnoreCase));
        var binaryVerdict = ResolveBinaryMappingVerdict(binaryCount, manualAssessment);
        var binarySummary = BuildBinaryMappingSummary(binaryCount, manualAssessment);
        yield return Item(
            "7.5.3",
            "Binary indication mapping",
            "Verify binary indication mapping against the active point database.",
            "Binary point records include stable index, label, value, quality, and source context.",
            "Run Integrity Poll and/or event poll, then operator verifies the binary indication list against the FAT wiring/signal list.",
            "Binary Input or Binary Output Status values plus explicit operator mapping assessment.",
            "Evidence is available when binary points exist; item is PASS only after operator marks mapping correct. It is FAIL when operator marks mapping incorrect.",
            binaryVerdict,
            binaryCount,
            binarySummary);

        var validSoeCount = soeEvents.Count(x => x.SourceTimestampKind == SourceTimestampKind.Valid);
        yield return Item(
            "7.5.4",
            "Protection event reporting",
            "Verify that protection events are received with forensic SOE timestamp context.",
            "SOE/event callback evidence is captured with source timestamp status.",
            "Trigger relay/protection event, then run event poll or receive unsolicited event callback.",
            "SOE rows with event class, point, value, timestamp status, and preferably valid source timestamp.",
            "Marked tested when SOE rows exist; upgraded to pass when at least one SOE row has a valid source timestamp.",
            validSoeCount > 0 ? ReportVerdict.Pass : soeEvents.Count > 0 ? ReportVerdict.PassWithWarning : ReportVerdict.NotTested,
            soeEvents.Count,
            validSoeCount > 0 ? $"{validSoeCount} SOE records include valid source timestamps." : soeEvents.Count > 0 ? "SOE records exist but timestamp quality needs review." : "No SOE evidence captured yet.");

        yield return Item(
            "7.5.5",
            "Non-operation verification",
            "Verify that expected non-operation conditions are documented.",
            "No unintended operation evidence is observed during the selected test step.",
            "Execute a dedicated negative test step, such as blocked command, disabled output, or inhibited operation scenario.",
            "A negative-test transaction showing request context and expected non-operation result.",
            "Marked tested when the guided non-operation test is executed; PASS when invalid/unsafe operation is rejected or produces no accepted operation evidence.",
            ResolveNonOperationVerdict(manualAssessment),
            manualAssessment.NonOperationTestExecuted ? 1 : 0,
            BuildNonOperationSummary(manualAssessment));

        yield return Item(
            "7.5.6",
            "Setting group status verification",
            "Verify reported setting group status where supported by the DUT.",
            "Setting group status evidence is captured and reviewed.",
            "Read the DUT setting group/status point according to the project point map.",
            "Mapped setting group status point value and source evidence.",
            "Not automated yet; app needs metadata that identifies which point represents setting group status.",
            ReportVerdict.NotTested,
            0,
            "Setting group status evidence is not yet captured by the current workflow.");

        var commandVerdict = manualAssessment.CommandSequenceExecuted
            ? manualAssessment.CommandSequenceCompleted == manualAssessment.CommandSequenceAttempted && manualAssessment.CommandSequenceAttempted > 0
                ? ReportVerdict.Pass
                : ReportVerdict.PassWithWarning
            : ResolveCommandVerdict(latestCommand);
        yield return Item(
            "7.5.7",
            "Setting group write / command lifecycle",
            "Verify command request, acceptance, feedback, and final lifecycle result.",
            "Command transaction reaches terminal result with explicit feedback evidence.",
            "Run the guided command sequence or send a binary command from Command Audit and wait for acceptance plus feedback/status evidence.",
            "Command transaction lifecycle entries with acceptance result, feedback evidence kind, latency, and final verdict.",
            "Marked tested when a guided command sequence or command transaction exists; verdict is derived from terminal result and feedback match.",
            commandVerdict,
            manualAssessment.CommandSequenceAttempted > 0 ? manualAssessment.CommandSequenceAttempted : latestCommand is null ? 0 : latestCommand.Lifecycle.Count,
            BuildCommandSummary(latestCommand, manualAssessment));

        var recoveryEvents = events.Count(x =>
            x.EventType.Contains("Disconnect", StringComparison.OrdinalIgnoreCase) ||
            x.EventType.Contains("Connect", StringComparison.OrdinalIgnoreCase) ||
            x.Detail.Contains("reconnect", StringComparison.OrdinalIgnoreCase));
        yield return Item(
            "7.5.8",
            "Communication recovery",
            "Verify recovery behavior after communication interruption.",
            "Disconnect and recovery evidence is captured with final communication state.",
            "Interrupt communication, reconnect, and capture the disconnect/reconnect sequence plus restored data flow.",
            "Connection/disconnection event rows and final communication evidence after recovery.",
            "Marked tested when the guided recovery test is executed; PASS when reconnect and post-recovery poll succeed.",
            ResolveRecoveryVerdict(manualAssessment, recoveryEvents),
            recoveryEvents + (manualAssessment.RecoveryTestExecuted ? 1 : 0),
            BuildRecoverySummary(manualAssessment, recoveryEvents));
    }

    private static FatTestItemResult Item(
        string code,
        string title,
        string objective,
        string criteria,
        string testMethod,
        string requiredEvidence,
        string recognitionRule,
        ReportVerdict verdict,
        int evidenceCount,
        string summary) => new()
        {
            ItemCode = code,
            Title = title,
            Objective = objective,
            AcceptanceCriteria = criteria,
            TestMethod = testMethod,
            RequiredEvidence = requiredEvidence,
            RecognitionRule = recognitionRule,
            Verdict = verdict,
            EvidenceCount = evidenceCount,
            EvidenceSummary = summary,
            Rationale = summary
        };

    private static ReportVerdict ResolveCommandVerdict(CommandTransaction? transaction)
    {
        if (transaction is null)
        {
            return ReportVerdict.NotTested;
        }

        if (transaction.FinalVerdict.Contains("Pass", StringComparison.OrdinalIgnoreCase) ||
            transaction.FinalVerdict.Contains("Accepted", StringComparison.OrdinalIgnoreCase) ||
            transaction.FinalVerdict.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            return transaction.FeedbackMatched ? ReportVerdict.Pass : ReportVerdict.PassWithWarning;
        }

        if (transaction.FinalVerdict.Contains("Fail", StringComparison.OrdinalIgnoreCase) ||
            transaction.AcceptanceResult.Contains("Fail", StringComparison.OrdinalIgnoreCase))
        {
            return ReportVerdict.Fail;
        }

        return transaction.IsTerminal ? ReportVerdict.Inconclusive : ReportVerdict.NotTested;
    }

    private static string BuildCommandSummary(CommandTransaction? transaction, ReportManualAssessment manualAssessment)
    {
        if (manualAssessment.CommandSequenceExecuted)
        {
            var summary = $"Guided command sequence attempted {manualAssessment.CommandSequenceAttempted} command(s), completed {manualAssessment.CommandSequenceCompleted}.";
            return string.IsNullOrWhiteSpace(manualAssessment.CommandSequenceRemarks)
                ? summary
                : $"{summary} Remarks: {manualAssessment.CommandSequenceRemarks}";
        }

        return transaction is null
            ? "No command transaction captured yet."
            : $"{transaction.PointLabel}: {transaction.FinalVerdict}, feedback {transaction.FeedbackEvidenceText}.";
    }

    private static ReportVerdict ResolveNonOperationVerdict(ReportManualAssessment manualAssessment)
    {
        if (!manualAssessment.NonOperationTestExecuted)
        {
            return ReportVerdict.NotTested;
        }

        return manualAssessment.NonOperationRejected ? ReportVerdict.Pass : ReportVerdict.Inconclusive;
    }

    private static string BuildNonOperationSummary(ReportManualAssessment manualAssessment)
    {
        if (!manualAssessment.NonOperationTestExecuted)
        {
            return "Guided non-operation workflow has not been executed in this snapshot.";
        }

        var summary = manualAssessment.NonOperationRejected
            ? "Guided invalid-index command was rejected or did not produce valid operation evidence."
            : "Guided invalid-index command did not produce a clear rejection; engineer review required.";
        return string.IsNullOrWhiteSpace(manualAssessment.NonOperationRemarks)
            ? summary
            : $"{summary} Remarks: {manualAssessment.NonOperationRemarks}";
    }

    private static ReportVerdict ResolveRecoveryVerdict(ReportManualAssessment manualAssessment, int recoveryEvents)
    {
        if (manualAssessment.RecoveryTestExecuted)
        {
            return manualAssessment.RecoveryRestored ? ReportVerdict.Pass : ReportVerdict.Fail;
        }

        return recoveryEvents > 1 ? ReportVerdict.PassWithWarning : ReportVerdict.NotTested;
    }

    private static string BuildRecoverySummary(ReportManualAssessment manualAssessment, int recoveryEvents)
    {
        if (manualAssessment.RecoveryTestExecuted)
        {
            var duration = manualAssessment.RecoveryDurationSeconds.HasValue
                ? $" in {manualAssessment.RecoveryDurationSeconds.Value:0.0}s"
                : string.Empty;
            var summary = manualAssessment.RecoveryRestored
                ? $"Guided recovery test restored communication{duration}."
                : "Guided recovery test did not restore communication.";
            return string.IsNullOrWhiteSpace(manualAssessment.RecoveryRemarks)
                ? summary
                : $"{summary} Remarks: {manualAssessment.RecoveryRemarks}";
        }

        return recoveryEvents > 1 ? "Recovery-related event evidence exists; operator review required." : "No recovery test evidence captured yet.";
    }

    private static ReportVerdict ResolveBinaryMappingVerdict(int binaryCount, ReportManualAssessment manualAssessment)
    {
        if (binaryCount == 0)
        {
            return ReportVerdict.NotTested;
        }

        return manualAssessment.BinaryIndicationMappingVerified switch
        {
            true => ReportVerdict.Pass,
            false => ReportVerdict.Fail,
            _ => ReportVerdict.Inconclusive
        };
    }

    private static string BuildBinaryMappingSummary(int binaryCount, ReportManualAssessment manualAssessment)
    {
        if (binaryCount == 0)
        {
            return "No binary indication evidence captured yet.";
        }

        var remarks = string.IsNullOrWhiteSpace(manualAssessment.BinaryIndicationRemarks)
            ? string.Empty
            : $" Remarks: {manualAssessment.BinaryIndicationRemarks}";

        return manualAssessment.BinaryIndicationMappingVerified switch
        {
            true => $"{binaryCount} binary-related point records were verified correct by operator.{remarks}",
            false => $"{binaryCount} binary-related point records require correction based on operator review.{remarks}",
            _ => $"{binaryCount} binary-related point records are available, but mapping has not been operator-verified yet."
        };
    }

    private static ReportVerdict ResolveOverallVerdict(IReadOnlyCollection<FatTestItemResult> items)
    {
        if (items.Any(x => x.Verdict == ReportVerdict.Fail))
        {
            return ReportVerdict.Fail;
        }

        if (items.Any(x => x.Verdict == ReportVerdict.PassWithWarning))
        {
            return ReportVerdict.PassWithWarning;
        }

        if (items.Any(x => x.Verdict == ReportVerdict.Inconclusive))
        {
            return ReportVerdict.PassWithWarning;
        }

        return items.Any(x => x.Verdict == ReportVerdict.Pass)
            ? ReportVerdict.Pass
            : ReportVerdict.NotTested;
    }

    private static string ResolveFatExecutionStatus(IReadOnlyCollection<FatTestItemResult> items)
    {
        var executed = items.Count(x => x.Verdict != ReportVerdict.NotTested);
        if (executed == 0)
        {
            return "NOT EXECUTED";
        }

        var open = items.Count(x => x.Verdict is ReportVerdict.NotTested or ReportVerdict.Inconclusive);
        return open == 0 ? "COMPLETE FAT EXECUTED" : "PARTIAL FAT EXECUTED";
    }

    private static string ResolveTechnicalResult(IReadOnlyCollection<FatTestItemResult> items)
    {
        var executed = items.Count(x => x.Verdict != ReportVerdict.NotTested);
        if (executed == 0)
        {
            return "NOT EXECUTED";
        }

        if (items.Any(x => x.Verdict == ReportVerdict.Fail))
        {
            return "FAIL";
        }

        if (items.Any(x => x.Verdict is ReportVerdict.NotTested or ReportVerdict.Inconclusive))
        {
            return "PASS WITH OPEN ITEMS";
        }

        if (items.Any(x => x.Verdict == ReportVerdict.PassWithWarning))
        {
            return "PASS WITH OBSERVATION";
        }

        return "PASS";
    }

    private static IEnumerable<string> BuildObservations(
        IReadOnlyCollection<ValueViewerRow> values,
        IReadOnlyCollection<SoeEventRow> soeEvents,
        IReadOnlyCollection<LinkTraceEntry> traces,
        IReadOnlyCollection<FatTestItemResult> fatItems,
        CommandTransaction? latestCommand)
    {
        var invalidTimestampValues = values.Count(x => x.SourceTimestampKind == SourceTimestampKind.Invalid);
        if (invalidTimestampValues > 0)
        {
            yield return $"{invalidTimestampValues} point records contain InvalidTime. Static/integrity reads may legitimately lack source time, but event records should be reviewed for source timestamp quality.";
        }

        var validSoeCount = soeEvents.Count(x => x.SourceTimestampKind == SourceTimestampKind.Valid);
        if (validSoeCount > 0)
        {
            yield return $"{validSoeCount} SOE records include valid source timestamps and should be prioritized for forensic event verification.";
        }

        if (traces.Count == 0)
        {
            yield return "No protocol trace evidence is attached to this snapshot; communication analysis is limited to state and value/event records.";
        }

        if (latestCommand is not null && latestCommand.FinalVerdict.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Latest command transaction reached Success with explicit lifecycle evidence.";
        }

        var openItems = fatItems.Where(x => x.Verdict is ReportVerdict.NotTested or ReportVerdict.Inconclusive).ToArray();
        if (openItems.Length > 0)
        {
            yield return $"{openItems.Length} FAT item(s) remain open or not tested; technical result should be read together with FAT completion status.";
        }
    }

    private static EventEvidenceRecord ToEventEvidence(EventLogEntry row) => new()
    {
        TimestampLocal = row.EventTimestampLocal,
        EvidenceType = row.EventType,
        PointLabel = row.PointLabel,
        Value = row.Value,
        Status = row.Status,
        Detail = row.EventTimestampBasis == "IED" ? row.Detail : $"{row.Detail} Time basis: {row.EventTimestampBasis}."
    };

    private static EventEvidenceRecord ToSoeEvidence(SoeEventRow row) => new()
    {
        TimestampLocal = row.ReceivedAtLocal,
        EvidenceType = $"{row.ReadType} {row.EventClass}".Trim(),
        PointLabel = row.PointLabel,
        Value = row.Value,
        Status = row.SourceTimestampText,
        Detail = row.Notes
    };

    private static PointEvidenceRecord ToPointEvidence(ValueViewerRow row) => new()
    {
        PointType = row.PointType,
        Index = row.Index,
        PointLabel = row.PointLabel,
        Value = row.Value,
        Quality = row.Quality,
        SourceTimestamp = row.SourceTimestampText,
        SourceReason = row.SourceReasonText
    };

    private static TraceEvidenceRecord ToTraceEvidence(LinkTraceEntry row) => new()
    {
        TimestampLocal = row.TimestampLocal,
        Level = row.Level,
        Direction = row.Direction,
        Summary = row.Summary
    };

    private static CommandEvidenceRecord ToCommandEvidence(CommandTransaction transaction) => new()
    {
        TransactionId = transaction.TransactionId,
        PointLabel = transaction.PointLabel,
        CommandMode = transaction.CommandMode,
        Operation = transaction.Operation,
        AcceptanceResult = transaction.AcceptanceResult,
        FeedbackResult = transaction.FeedbackResult,
        FinalVerdict = transaction.FinalVerdict,
        FeedbackEvidence = transaction.FeedbackEvidenceText,
        FeedbackLatency = transaction.FeedbackLatencyText
    };
}
