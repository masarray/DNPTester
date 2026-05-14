namespace Dnp3MasterTester.Models.Reports;

public sealed record EventEvidenceRecord
{
    public DateTime TimestampLocal { get; init; }
    public string EvidenceType { get; init; } = string.Empty;
    public string PointLabel { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
