namespace Dnp3MasterTester.Models.Reports;

public sealed record CommandEvidenceRecord
{
    public string TransactionId { get; init; } = string.Empty;
    public string PointLabel { get; init; } = string.Empty;
    public string CommandMode { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string AcceptanceResult { get; init; } = string.Empty;
    public string FeedbackResult { get; init; } = string.Empty;
    public string FinalVerdict { get; init; } = string.Empty;
    public string FeedbackEvidence { get; init; } = string.Empty;
    public string FeedbackLatency { get; init; } = string.Empty;
}
