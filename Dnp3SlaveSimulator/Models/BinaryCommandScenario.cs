namespace Dnp3SlaveSimulator.Models;

public sealed class BinaryCommandScenario
{
    public bool IsEnabled { get; set; }
    public ushort FeedbackIndex { get; set; }
    public Dnp3OutstationPointType FeedbackPointType { get; set; } = Dnp3OutstationPointType.BinaryOutputStatus;
    public BinaryCommandBehavior Behavior { get; set; } = BinaryCommandBehavior.SuccessMatch;
    public int FeedbackDelayMs { get; set; } = 800;

    public BinaryCommandScenario Clone()
    {
        return (BinaryCommandScenario)MemberwiseClone();
    }
}
