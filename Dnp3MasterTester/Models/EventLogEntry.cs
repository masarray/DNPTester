namespace Dnp3MasterTester.Models;

public sealed class EventLogEntry
{
    public DateTime TimestampLocal { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string PointType { get; set; } = string.Empty;
    public ushort? Index { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ScadaTag { get; set; } = string.Empty;
    public string RawValue { get; set; } = string.Empty;
    public string RawPreviousValue { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string PreviousValue { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public SourceReason SourceReason { get; set; } = SourceReason.Unknown;
    public string Detail { get; set; } = string.Empty;
    public string SourceReasonText => SourceReason.ToString();
    public string PointLabel => Index.HasValue
        ? string.IsNullOrWhiteSpace(ScadaTag) ? $"{PointType} {Index}" : $"{ScadaTag} | {DisplayName}"
        : PointType;
}
