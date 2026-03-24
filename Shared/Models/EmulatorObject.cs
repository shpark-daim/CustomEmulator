using System.Text.Json.Serialization;

namespace Shared.Models;

public class EmulatorObject
{
    [JsonPropertyName("unit_id")]
    public string UnitId { get; set; } = "";

    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Robot";

    [JsonPropertyName("position")]
    public Position Position { get; set; } = new();

    [JsonPropertyName("communication")]
    public CommunicationConfig Communication { get; set; } = new();

    [JsonPropertyName("properties")]
    public Dictionary<string, ObjectProperty> Properties { get; set; } = [];
}

public class Position
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}
