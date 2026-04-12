namespace Dnp3MasterTester.Models;

public sealed class CommandFeedbackMapping
{
    public bool IsEnabled { get; set; } = true;
    public ushort CommandIndex { get; set; }
    public string CommandPointType { get; set; } = "Binary Output";
    public string CommandDisplayName { get; set; } = string.Empty;
    public ushort FeedbackIndex { get; set; }
    public string FeedbackPointType { get; set; } = "Binary Input";
    public string FeedbackDisplayName { get; set; } = string.Empty;
    public string DefaultCommandMode { get; set; } = "DirectOperate";
    public int TimeoutMs { get; set; } = 5000;
}
