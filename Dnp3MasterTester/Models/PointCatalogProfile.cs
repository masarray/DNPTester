using System.Text.Json.Serialization;

namespace Dnp3MasterTester.Models;

public sealed class PointCatalogProfile
{
    public string Name { get; set; } = string.Empty;
    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;
    public List<PointCatalogEntry> Points { get; set; } = [];
    public List<CommandFeedbackMapping> CommandMappings { get; set; } = [];

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? base.ToString() ?? string.Empty : Name;
}
