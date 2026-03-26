using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models;

public class ObjectProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string"; // float | int | string | bool | enum | button

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }

    /// <summary>enum type only — the selectable values (e.g. ["Auto","Manual","Idle"])</summary>
    [JsonPropertyName("enum_values")]
    public List<string> EnumValues { get; set; } = [];

    /// <summary>button type only — MQTT topic to publish to when clicked (empty = all publish topics)</summary>
    [JsonPropertyName("on_click_topic")]
    public string OnClickTopic { get; set; } = "";

    /// <summary>button type only — JSON payload to publish when clicked</summary>
    [JsonPropertyName("on_click_payload")]
    public string OnClickPayload { get; set; } = "{}";

    /// <summary>true이면 Emulator UI에서 값 변경 불가 (표시 전용)</summary>
    [JsonPropertyName("readonly")]
    public bool IsReadOnly { get; set; } = false;
}
