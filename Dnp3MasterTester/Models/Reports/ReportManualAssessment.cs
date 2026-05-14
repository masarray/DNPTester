namespace Dnp3MasterTester.Models.Reports;

public sealed class ReportManualAssessment
{
    public bool? BinaryIndicationMappingVerified { get; set; }
    public string BinaryIndicationRemarks { get; set; } = string.Empty;
    public bool? AnalogValueVerificationPassed { get; set; }
    public string AnalogValueRemarks { get; set; } = string.Empty;
    public bool CommandSequenceExecuted { get; set; }
    public int CommandSequenceAttempted { get; set; }
    public int CommandSequenceCompleted { get; set; }
    public string CommandSequenceRemarks { get; set; } = string.Empty;
    public bool NonOperationTestExecuted { get; set; }
    public bool NonOperationRejected { get; set; }
    public string NonOperationRemarks { get; set; } = string.Empty;
    public bool RecoveryTestExecuted { get; set; }
    public bool RecoveryRestored { get; set; }
    public double? RecoveryDurationSeconds { get; set; }
    public string RecoveryRemarks { get; set; } = string.Empty;
}
