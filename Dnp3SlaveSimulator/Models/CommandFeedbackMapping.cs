namespace Dnp3SlaveSimulator.Models;

public sealed class CommandFeedbackMapping
{
    public bool IsEnabled { get; set; } = true;
    public ushort CommandIndex { get; set; }
    public Dnp3OutstationPointType CommandPointType { get; set; } = Dnp3OutstationPointType.BinaryOutputStatus;
    public string CommandDisplayName { get; set; } = string.Empty;
    public ushort FeedbackIndex { get; set; }
    public Dnp3OutstationPointType FeedbackPointType { get; set; } = Dnp3OutstationPointType.BinaryInput;
    public string FeedbackDisplayName { get; set; } = string.Empty;
    public int FeedbackDelayMs { get; set; } = 800;
}
