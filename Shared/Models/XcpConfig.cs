using System.Text.Json.Serialization;

namespace Shared.Models;

public class XcpConfig
{
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = "xflow";

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "rcp";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "v1";

    /// <summary>비어 있으면 런타임에 UnitId를 사용합니다.</summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    /// <summary>
    /// 선택 구독 커맨드 목록. (start / stop / pause / resume / abort / end)
    /// status · sync · auto · manual 은 항상 구독됩니다.
    /// </summary>
    [JsonPropertyName("optional_commands")]
    public List<string> OptionalCommands { get; set; } = [];
}
