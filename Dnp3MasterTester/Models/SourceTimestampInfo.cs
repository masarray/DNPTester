namespace Dnp3MasterTester.Models;

public sealed record SourceTimestampInfo(SourceTimestampKind Kind, DateTime? LocalTime, string TimeQuality)
{
    public static SourceTimestampInfo Unknown(string timeQuality = "-") => new(SourceTimestampKind.Unknown, null, timeQuality);

    public static SourceTimestampInfo NotSupplied(string timeQuality = "-") => new(SourceTimestampKind.NotSupplied, null, timeQuality);

    public static SourceTimestampInfo Invalid(string timeQuality) => new(SourceTimestampKind.Invalid, null, timeQuality);

    public static SourceTimestampInfo Valid(DateTime localTime, string timeQuality) => new(SourceTimestampKind.Valid, localTime, timeQuality);

    public string DisplayText => Kind switch
    {
        SourceTimestampKind.Valid when LocalTime.HasValue => LocalTime.Value.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        SourceTimestampKind.Invalid => "Invalid",
        SourceTimestampKind.NotSupplied => "Not Supplied",
        _ => "Unknown"
    };
}
