using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace RevealLoader;

/// <summary>Проверяет доступность новой версии лаунчера по манифесту update.json.</summary>
public static class UpdateService
{
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/DisRiks/RevealLoader/main/update.json";

    /// <summary>Текущая версия лаунчера из атрибута сборки.</summary>
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    /// <summary>
    /// Читает манифест и возвращает версию лаунчера с сервера, если она новее текущей.
    /// При любой ошибке (нет сети, битый ответ) возвращает null.
    /// </summary>
    public static async Task<string?> GetAvailableLauncherVersionAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            string json = await http.GetStringAsync(ManifestUrl);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("launcher", out var launcher)
                || !launcher.TryGetProperty("version", out var versionElement))
                return null;

            string remote = versionElement.GetString() ?? "";
            return IsNewer(remote, CurrentVersion) ? remote : null;
        }
        catch
        {
            // Нет сети или манифест недоступен — считаем, что обновлений нет.
            return null;
        }
    }

    /// <summary>Сравнивает версии по сегментам: 1.2.10 новее 1.2.9.</summary>
    private static bool IsNewer(string remote, string current)
    {
        int[] r = Parse(remote);
        int[] c = Parse(current);
        int length = Math.Max(r.Length, c.Length);
        for (int i = 0; i < length; i++)
        {
            int rv = i < r.Length ? r[i] : 0;
            int cv = i < c.Length ? c[i] : 0;
            if (rv != cv) return rv > cv;
        }
        return false;
    }

    private static int[] Parse(string version) => version
        .Split('.', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => int.TryParse(p, out int n) ? n : 0)
        .ToArray();
}
