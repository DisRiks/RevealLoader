using System.IO;
using System.Text.Json;
using System.Windows;

namespace RevealLoader;

/// <summary>Читает вшитую в exe историю версий (Resources/history.json).</summary>
public static class VersionHistoryService
{
    private const string ResourcePath = "resources/history.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Возвращает список записей истории. При любой ошибке — пустой список.</summary>
    public static IReadOnlyList<VersionEntry> Load()
    {
        try
        {
            var uri = new Uri(ResourcePath, UriKind.Relative);
            using var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream is null)
                return Array.Empty<VersionEntry>();

            var history = JsonSerializer.Deserialize<VersionHistory>(stream, JsonOptions);
            return history?.Entries ?? (IReadOnlyList<VersionEntry>)Array.Empty<VersionEntry>();
        }
        catch
        {
            return Array.Empty<VersionEntry>();
        }
    }
}
