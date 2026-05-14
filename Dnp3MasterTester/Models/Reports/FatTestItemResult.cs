namespace Dnp3MasterTester.Models.Reports;

public sealed record FatTestItemResult
{
    public string ItemCode { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Objective { get; init; } = string.Empty;
    public string AcceptanceCriteria { get; init; } = string.Empty;
    public string TestMethod { get; init; } = string.Empty;
    public string RequiredEvidence { get; init; } = string.Empty;
    public string RecognitionRule { get; init; } = string.Empty;
    public ReportVerdict Verdict { get; init; } = ReportVerdict.NotTested;
    public int EvidenceCount { get; init; }
    public string EvidenceSummary { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;

    public string VerdictText => Verdict switch
    {
        ReportVerdict.Pass => "PASS",
        ReportVerdict.PassWithWarning => "PASS WITH WARNING",
        ReportVerdict.Fail => "FAIL",
        ReportVerdict.Inconclusive => "INCONCLUSIVE",
        _ => "NOT TESTED"
    };
}
