using System.Text.Json.Serialization;

namespace Shared.Models;

public class ParseField
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string"; // float, string, bool

    [JsonPropertyName("maps_to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MapsTo { get; set; }
}
