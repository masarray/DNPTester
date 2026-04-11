namespace Dnp3MasterTester.Models;

public sealed class CommandLifecycleEntry
{
    public DateTime TimestampLocal { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
