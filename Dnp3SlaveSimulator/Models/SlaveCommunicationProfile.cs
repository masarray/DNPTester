namespace Dnp3SlaveSimulator.Models;

public sealed class SlaveCommunicationProfile
{
    public bool EnableUnsolicited { get; set; }
    public bool UnsolicitedClass1 { get; set; } = true;
    public bool UnsolicitedClass2 { get; set; }
    public bool UnsolicitedClass3 { get; set; }
}
