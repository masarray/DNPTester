namespace Dnp3MasterTester.Models.Reports;

public sealed record FatTestSessionSnapshot
{
    public string ReportId { get; init; } = string.Empty;
    public DateTime GeneratedAtLocal { get; init; }
    public DateTime? FinalizedAtLocal { get; init; }
    public bool IsFinalized { get; init; }
    public ReportBrandingSettings Branding { get; init; } = new();
    public string ConnectionProfile { get; init; } = string.Empty;
    public string ConnectionTarget { get; init; } = string.Empty;
    public string ConnectionState { get; init; } = string.Empty;
    public string ConnectionDetail { get; init; } = string.Empty;
    public string PollingProfile { get; init; } = string.Empty;
    public string PointCatalogProfileName { get; init; } = string.Empty;
    public ReportVerdict OverallVerdict { get; init; } = ReportVerdict.Inconclusive;
    public string FatExecutionStatus { get; init; } = "NOT EXECUTED";
    public string TechnicalResult { get; init; } = "NOT EXECUTED";
    public IReadOnlyList<FatTestItemResult> FatItems { get; init; } = Array.Empty<FatTestItemResult>();
    public IReadOnlyList<EventEvidenceRecord> EventEvidence { get; init; } = Array.Empty<EventEvidenceRecord>();
    public IReadOnlyList<EventEvidenceRecord> SoeEvidence { get; init; } = Array.Empty<EventEvidenceRecord>();
    public IReadOnlyList<TraceEvidenceRecord> TraceEvidence { get; init; } = Array.Empty<TraceEvidenceRecord>();
    public IReadOnlyList<PointEvidenceRecord> PointEvidence { get; init; } = Array.Empty<PointEvidenceRecord>();
    public IReadOnlyList<CommandEvidenceRecord> CommandEvidence { get; init; } = Array.Empty<CommandEvidenceRecord>();
    public IReadOnlyList<string> Observations { get; init; } = Array.Empty<string>();
    public int TraceEvidenceCount { get; init; }
    public int ExecutedItemCount => FatItems.Count(x => x.Verdict != ReportVerdict.NotTested);
    public int PassedItemCount => FatItems.Count(x => x.Verdict == ReportVerdict.Pass);
    public int WarningItemCount => FatItems.Count(x => x.Verdict == ReportVerdict.PassWithWarning);
    public int FailedItemCount => FatItems.Count(x => x.Verdict == ReportVerdict.Fail);
    public int InconclusiveItemCount => FatItems.Count(x => x.Verdict == ReportVerdict.Inconclusive);
    public int NotTestedItemCount => FatItems.Count(x => x.Verdict == ReportVerdict.NotTested);
    public int OpenItemCount => InconclusiveItemCount + NotTestedItemCount;

    public string GeneratedAtText => GeneratedAtLocal.ToString("yyyy-MM-dd HH:mm");
    public string FinalizedAtText => FinalizedAtLocal?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Live preview";
    public string EvidenceStateText => IsFinalized ? "Evidence frozen" : "Live evidence preview";
    public string OverallVerdictText => OverallVerdict switch
    {
        ReportVerdict.Pass => "PASS",
        ReportVerdict.PassWithWarning => "PASS WITH WARNING",
        ReportVerdict.Fail => "FAIL",
        ReportVerdict.Inconclusive => "INCONCLUSIVE",
        _ => "NOT TESTED"
    };
}
