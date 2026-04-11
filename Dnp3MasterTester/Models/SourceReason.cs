namespace Dnp3MasterTester.Models;

public enum SourceReason
{
    Unknown,
    StartupIntegrity,
    PeriodicEventPoll,
    PeriodicStaticRefresh,
    ManualEventPoll,
    ManualIntegrity,
    AutoEventScan,
    Unsolicited,
    CommandResponse
}
