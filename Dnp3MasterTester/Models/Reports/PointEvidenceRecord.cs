namespace Dnp3MasterTester.Models.Reports;

public sealed record PointEvidenceRecord
{
    public string PointType { get; init; } = string.Empty;
    public ushort Index { get; init; }
    public string PointLabel { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public string SourceTimestamp { get; init; } = string.Empty;
    public string SourceReason { get; init; } = string.Empty;
}
