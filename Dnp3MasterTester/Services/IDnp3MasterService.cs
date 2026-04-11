using Dnp3MasterTester.Models;

namespace Dnp3MasterTester.Services;

public interface IDnp3MasterService
{
    event EventHandler<ConnectionStatusSnapshot>? ConnectionStateChanged;
    event EventHandler<ValueViewerRow>? ValueReceived;
    event EventHandler<EventLogEntry>? EventLogReceived;
    event EventHandler<SoeEventRow>? SoeEventReceived;
    event EventHandler<LinkTraceEntry>? LinkTraceReceived;

    bool IsConnected { get; }

    Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task DemandEventPollAsync();
    Task RunIntegrityPollAsync();
    Task CheckLinkStatusAsync();
}
