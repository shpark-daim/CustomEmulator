using System.Text.Json.Serialization;

namespace Shared.Models;

public class EmulatorConfig
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("brokers")]
    public Dictionary<string, BrokerConfig> Brokers { get; set; } = new();

    [JsonPropertyName("objects")]
    public List<EmulatorObject> Objects { get; set; } = new();
}
