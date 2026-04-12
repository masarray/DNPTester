using System.Text.Json.Serialization;

namespace Dnp3SlaveSimulator.Models;

public sealed class SignalDatabaseProfile
{
    public string Name { get; set; } = string.Empty;

    public SlaveCommunicationProfile Communication { get; set; } = new();

    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    public List<Dnp3SimulatorSignal> Signals { get; set; } = [];
    public List<CommandFeedbackMapping> CommandMappings { get; set; } = [];
}
