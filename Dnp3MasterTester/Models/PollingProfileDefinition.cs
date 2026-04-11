namespace Dnp3MasterTester.Models;

public sealed record PollingProfileDefinition(
    PollingProfileKind Kind,
    int FastEventPollSeconds,
    int StaticRefreshSeconds,
    bool EnableSlowStaticRefresh,
    bool EnableAutoEventScan,
    bool EnableUnsolicited,
    bool EnableStartupIntegrity,
    TimeSpan KeepAliveTimeout)
{
    public string Summary =>
        $"{Kind} | Startup={(EnableStartupIntegrity ? "On" : "Off")} | Event={FastEventPollSeconds}s | Static={(EnableSlowStaticRefresh ? $"{StaticRefreshSeconds}s" : "Off")} | AutoIIN={(EnableAutoEventScan ? "On" : "Off")} | Unsol={(EnableUnsolicited ? "On" : "Off")}";
}
