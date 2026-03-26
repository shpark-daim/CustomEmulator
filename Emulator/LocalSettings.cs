using System.IO;
using System.Text.Json;

namespace Emulator;

public class LocalSettings
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public int    RestPort        { get; set; } = 5555;
    public string MqttBrokerHost  { get; set; } = "localhost";
    public int    MqttBrokerPort  { get; set; } = 1883;

    public static LocalSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(FilePath))
                       ?? new LocalSettings();
        }
        catch { }
        return new LocalSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
