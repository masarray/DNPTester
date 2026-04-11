namespace Dnp3MasterTester.Models;

public sealed class LinkTraceEntry
{
    public DateTime TimestampLocal { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
