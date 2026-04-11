namespace Dnp3SlaveSimulator.Models;

public enum BinaryCommandBehavior
{
    SuccessMatch,
    SuccessNoFeedback,
    SuccessDelayedMatch,
    SuccessMismatch,
    Reject
}
