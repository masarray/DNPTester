namespace Dnp3MasterTester.Models;

public sealed class EventLogEntry
{
    public DateTime TimestampLocal { get; set; }
    public DateTime? SourceTimestampLocal { get; set; }
    public SourceTimestampKind SourceTimestampKind { get; set; } = SourceTimestampKind.Unknown;
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
    public DateTime EventTimestampLocal => SourceTimestampKind == SourceTimestampKind.Valid && SourceTimestampLocal.HasValue
        ? SourceTimestampLocal.Value
        : TimestampLocal;
    public string EventTimestampBasis => SourceTimestampKind == SourceTimestampKind.Valid && SourceTimestampLocal.HasValue
        ? "IED"
        : "Captured";
    public string SourceTimestampText => SourceTimestampKind switch
    {
        SourceTimestampKind.Valid when SourceTimestampLocal.HasValue => SourceTimestampLocal.Value.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        SourceTimestampKind.Invalid => "Invalid",
        SourceTimestampKind.NotSupplied => "Not Supplied",
        _ => "Unknown"
    };
    public string SourceReasonText => SourceReason.ToString();
    public string PointLabel => Index.HasValue
        ? !string.IsNullOrWhiteSpace(ScadaTag) && !string.IsNullOrWhiteSpace(DisplayName)
            ? $"{ScadaTag} | {DisplayName}"
            : !string.IsNullOrWhiteSpace(DisplayName)
                ? DisplayName
                : $"{PointType} {Index}"
        : PointType;
}
