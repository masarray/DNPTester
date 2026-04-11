namespace Dnp3MasterTester.Models;

public sealed class ConnectionStatusSnapshot
{
    public string State { get; set; } = "Idle";
    public string Detail { get; set; } = "Disconnected";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
