namespace Dnp3MasterTester.Models.Reports;

public sealed record TraceEvidenceRecord
{
    public DateTime TimestampLocal { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}
