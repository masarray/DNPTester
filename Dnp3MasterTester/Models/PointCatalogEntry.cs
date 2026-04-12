namespace Dnp3MasterTester.Models;

public sealed class PointCatalogEntry
{
    public ushort Index { get; set; }
    public string PointType { get; set; } = string.Empty;
    public string ObjectVariation { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ScadaTag { get; set; } = string.Empty;
    public string EngineeringRef { get; set; } = string.Empty;
    public string DeviceRef { get; set; } = string.Empty;
    public string StateTextOff { get; set; } = string.Empty;
    public string StateTextOn { get; set; } = string.Empty;
    public string IedModel { get; set; } = string.Empty;
    public string DrawingRef { get; set; } = string.Empty;
    public string DnpClass { get; set; } = string.Empty;
    public string DataConcentratorUnit { get; set; } = string.Empty;
    public string IoRef { get; set; } = string.Empty;
    public bool FeedbackMappingEnabled { get; set; }
    public ushort? FeedbackIndex { get; set; }
    public string FeedbackPointType { get; set; } = string.Empty;
    public string FeedbackDisplayName { get; set; } = string.Empty;
    public string DefaultCommandMode { get; set; } = "DirectOperate";
    public int TimeoutMs { get; set; } = 5000;
    public string FeedbackControl => FeedbackMappingEnabled && FeedbackIndex.HasValue
        ? string.IsNullOrWhiteSpace(FeedbackDisplayName)
            ? $"{FeedbackPointType} {FeedbackIndex.Value}"
            : $"{FeedbackIndex.Value} | {FeedbackDisplayName}"
        : string.Empty;
    public string DisplayLabel => string.IsNullOrWhiteSpace(ScadaTag) ? DisplayName : $"{ScadaTag} | {DisplayName}";
}
