using System.Text.Json;
using Shared.Models;

namespace Shared;

public static class ConfigSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static EmulatorConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EmulatorConfig>(json, Options) ?? new EmulatorConfig();
    }

    public static void Save(EmulatorConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(path, json);
    }
}
