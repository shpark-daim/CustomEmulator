using System.Text.Json.Serialization;

namespace Shared.Models;

public class BrokerConfig
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "mqtt";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 1883;
}
