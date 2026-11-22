using System.IO;
using System.Text.Json;

namespace RevealLoader;

public static class SettingsService
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static LauncherSettings Load()
    {
        if (!File.Exists(ConfigPath))
            return new LauncherSettings();

        string json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<LauncherSettings>(json, JsonOptions)
               ?? new LauncherSettings();
    }

    public static void Save(LauncherSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
