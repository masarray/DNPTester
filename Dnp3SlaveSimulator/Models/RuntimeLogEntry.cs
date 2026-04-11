namespace Dnp3SlaveSimulator.Models;

public sealed class RuntimeLogEntry
{
    public DateTime TimestampLocal { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
