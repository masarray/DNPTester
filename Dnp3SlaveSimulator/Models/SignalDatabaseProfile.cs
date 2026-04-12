using System.Text.Json.Serialization;

namespace Dnp3SlaveSimulator.Models;

public sealed class SignalDatabaseProfile
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    public List<Dnp3SimulatorSignal> Signals { get; set; } = [];
}
