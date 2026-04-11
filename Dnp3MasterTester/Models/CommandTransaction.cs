namespace Dnp3MasterTester.Models;

public sealed class CommandTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string PointType { get; init; } = "Binary Output";
    public ushort PointIndex { get; init; }
    public string CommandMode { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public DateTime PreparedAtLocal { get; init; }
    public DateTime? RequestedAtLocal { get; init; }
    public DateTime? AcceptanceAtLocal { get; init; }
    public DateTime? FeedbackAtLocal { get; init; }
    public string AcceptanceResult { get; init; } = "Pending";
    public string FeedbackResult { get; init; } = "Pending";
    public string FinalVerdict { get; init; } = "In Progress";
    public bool FeedbackMatched { get; init; }
    public CommandFeedbackEvidenceKind FeedbackEvidenceKind { get; init; } = CommandFeedbackEvidenceKind.None;
    public int? AcceptanceLatencyMs { get; init; }
    public int? FeedbackLatencyMs { get; init; }
    public bool IsTerminal { get; init; }
    public IReadOnlyList<CommandLifecycleEntry> Lifecycle { get; init; } = Array.Empty<CommandLifecycleEntry>();

    public string PreparedAtText => PreparedAtLocal.ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string RequestedAtText => RequestedAtLocal?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "-";
    public string AcceptanceAtText => AcceptanceAtLocal?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "-";
    public string FeedbackAtText => FeedbackAtLocal?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "-";
    public string FeedbackMatchedText => FeedbackMatched ? "Yes" : "No";
    public string FeedbackEvidenceText => FeedbackEvidenceKind switch
    {
        CommandFeedbackEvidenceKind.CommandEvent => "Command Event",
        CommandFeedbackEvidenceKind.StatusChange => "Status Change",
        CommandFeedbackEvidenceKind.StatusReadSimpleRule => "Status Read (Simple Rule)",
        _ => "-"
    };
    public string AcceptanceLatencyText => AcceptanceLatencyMs.HasValue ? $"{AcceptanceLatencyMs.Value} ms" : "-";
    public string FeedbackLatencyText => FeedbackLatencyMs.HasValue ? $"{FeedbackLatencyMs.Value} ms" : "-";
}
