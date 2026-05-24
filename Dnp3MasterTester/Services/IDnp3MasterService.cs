using Dnp3MasterTester.Models;

namespace Dnp3MasterTester.Services;

public interface IDnp3MasterService
{
    event EventHandler<ConnectionStatusSnapshot>? ConnectionStateChanged;
    event EventHandler<CommandTransaction>? CommandTransactionUpdated;
    event EventHandler<ValueViewerRow>? ValueReceived;
    event EventHandler<EventLogEntry>? EventLogReceived;
    event EventHandler<SoeEventRow>? SoeEventReceived;
    event EventHandler<LinkTraceEntry>? LinkTraceReceived;

    bool IsConnected { get; }
    bool HasDeviceResponse { get; }
    bool IsOperationalReady { get; }

    Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task DemandEventPollAsync();
    Task RunIntegrityPollAsync();
    Task CheckLinkStatusAsync();
    Task ExecuteBinaryControlAsync(
        ushort index,
        CommandMode mode,
        OpType operation,
        DateTime preparedAtLocal,
        string? expectedFeedbackPointType = null,
        ushort? expectedFeedbackIndex = null,
        int? correlationWindowMs = null);
}
