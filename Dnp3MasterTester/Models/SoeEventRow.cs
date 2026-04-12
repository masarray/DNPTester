namespace Dnp3MasterTester.Models;

public sealed class SoeEventRow
{
    public DateTime ReceivedAtLocal { get; set; }
    public DateTime? SourceTimestampLocal { get; set; }
    public SourceTimestampKind SourceTimestampKind { get; set; } = SourceTimestampKind.Unknown;
    public string ReadType { get; set; } = string.Empty;
    public string EventClass { get; set; } = string.Empty;
    public string PointType { get; set; } = string.Empty;
    public ushort Index { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ScadaTag { get; set; } = string.Empty;
    public string RawValue { get; set; } = string.Empty;
    public string RawPreviousValue { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string PreviousValue { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Flags { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Variation { get; set; } = string.Empty;
    public string Qualifier { get; set; } = string.Empty;
    public bool IsBroadcast { get; set; }
    public SourceReason SourceReason { get; set; } = SourceReason.Unknown;
    public string Notes { get; set; } = string.Empty;
    public string SourceReasonText => SourceReason.ToString();
    public string PointLabel => !string.IsNullOrWhiteSpace(ScadaTag) && !string.IsNullOrWhiteSpace(DisplayName)
        ? $"{ScadaTag} | {DisplayName}"
        : !string.IsNullOrWhiteSpace(DisplayName)
            ? DisplayName
            : $"{PointType} {Index}";
    public string SourceTimestampText => SourceTimestampKind switch
    {
        SourceTimestampKind.Valid when SourceTimestampLocal.HasValue => SourceTimestampLocal.Value.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        SourceTimestampKind.Invalid => "Invalid",
        SourceTimestampKind.NotSupplied => "Not Supplied",
        _ => "Unknown"
    };
}
