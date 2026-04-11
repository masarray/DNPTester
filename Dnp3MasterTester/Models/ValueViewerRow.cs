namespace Dnp3MasterTester.Models;

public sealed class ValueViewerRow
{
    public string PointType { get; set; } = string.Empty;
    public ushort Index { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Flags { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public DateTime ReceivedAtLocal { get; set; }
    public DateTime? SourceTimestampLocal { get; set; }
    public SourceTimestampKind SourceTimestampKind { get; set; } = SourceTimestampKind.Unknown;
    public string Source { get; set; } = string.Empty;
    public SourceReason SourceReason { get; set; } = SourceReason.Unknown;
    public string SourceReasonText => SourceReason.ToString();
    public string SourceTimestampText => SourceTimestampKind switch
    {
        SourceTimestampKind.Valid when SourceTimestampLocal.HasValue => SourceTimestampLocal.Value.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        SourceTimestampKind.Invalid => "Invalid",
        SourceTimestampKind.NotSupplied => "Not Supplied",
        _ => "Unknown"
    };
}
