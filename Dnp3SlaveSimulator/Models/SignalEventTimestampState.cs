namespace Dnp3SlaveSimulator.Models;

public sealed record SignalEventTimestampState(
    SignalEventTimestampStateKind Kind,
    ulong? UnixMilliseconds,
    DateTime? CapturedLocal)
{
    public static SignalEventTimestampState Invalid() => new(SignalEventTimestampStateKind.Invalid, null, null);

    public static SignalEventTimestampState Synchronized(DateTime capturedLocal)
    {
        var unixMilliseconds = (ulong)new DateTimeOffset(capturedLocal).ToUnixTimeMilliseconds();
        return new SignalEventTimestampState(SignalEventTimestampStateKind.Synchronized, unixMilliseconds, capturedLocal);
    }
}
