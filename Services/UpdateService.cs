using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevealLoader;

/// <summary>Данные о доступном обновлении лаунчера из манифеста.</summary>
public sealed record LauncherUpdate(string Version, string Url, string Sha256, long Size);

/// <summary>Проверяет, скачивает и устанавливает обновления лаунчера по манифесту update.json.</summary>
public static class UpdateService
{
    private const byte ManifestUrlXorKey = 0x5A;
    private static readonly byte[] EncodedManifestUrl =
    {
        50, 46, 46, 42, 41, 96, 117, 117, 40, 59, 45, 116, 61, 51, 46, 50,
        47, 56, 47, 41, 63, 40, 57, 53, 52, 46, 63, 52, 46, 116, 57, 53,
        55, 117, 30, 51, 41, 8, 51, 49, 41, 117, 8, 63, 44, 63, 59, 54,
        22, 53, 59, 62, 63, 40, 117, 55, 59, 51, 52, 117, 47, 42, 62, 59,
        46, 63, 116, 48, 41, 53, 52
    };

    public static string ManifestUrl { get; } = DecodeManifestUrl();

    private static string DecodeManifestUrl()
    {
        var characters = new char[EncodedManifestUrl.Length];
        for (int i = 0; i < EncodedManifestUrl.Length; i++)
            characters[i] = (char)(EncodedManifestUrl[i] ^ ManifestUrlXorKey);
        return new string(characters);
    }

    /// <summary>Текущая версия лаунчера из атрибута сборки.</summary>
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    /// <summary>
    /// Читает манифест и возвращает данные обновления, если версия на сервере новее текущей
    /// и указана ссылка на файл. При любой ошибке (нет сети, битый ответ) возвращает null.
    /// </summary>
    public static async Task<LauncherUpdate?> GetLauncherUpdateAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            string json = await http.GetStringAsync(ManifestUrl);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("launcher", out var launcher))
                return null;

            string remote = launcher.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString() ?? ""
                : "";
            if (!IsNewer(remote, CurrentVersion))
                return null;

            string url = launcher.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString() ?? ""
                : "";
            // Обновление без ссылки установить нельзя — не блокируем лаунчер.
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string sha = launcher.TryGetProperty("sha256", out var shaElement)
                ? shaElement.GetString() ?? ""
                : "";
            long size = launcher.TryGetProperty("size", out var sizeElement)
                        && sizeElement.TryGetInt64(out long parsedSize)
                ? parsedSize
                : 0;

            return new LauncherUpdate(remote, url, sha, size);
        }
        catch
        {
            // Нет сети или манифест недоступен — считаем, что обновлений нет.
            return null;
        }
    }

    /// <summary>Скачивает файл обновления во временную папку и возвращает путь к нему.</summary>
    public static async Task<string> DownloadAsync(
        LauncherUpdate update, IProgress<double>? progress, CancellationToken token)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"RevealLoader-{update.Version}.exe");
        await DownloadToFileAsync(update.Url, update.Size, tempPath, progress, token);
        return tempPath;
    }

    /// <summary>Скачивает файл по ссылке в указанный путь с отчётом о прогрессе.</summary>
    public static async Task DownloadToFileAsync(
        string url, long expectedSize, string destination,
        IProgress<double>? progress, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        long total = expectedSize > 0 ? expectedSize : response.Content.Headers.ContentLength ?? 0;

        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            if (total > 0)
                progress?.Report((double)received / total);
        }
    }

    /// <summary>Проверяет SHA-256 скачанного файла. Пустой ожидаемый хеш — проверка пропускается.</summary>
    public static bool VerifyHash(string filePath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return true;

        using var stream = File.OpenRead(filePath);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actual, expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Подменяет текущий exe скачанным файлом (текущий уходит в <имя>.old.exe)
    /// и перезапускает лаунчер.
    /// </summary>
    public static void ApplyAndRestart(string downloadedPath)
    {
        string currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к лаунчеру.");

        // Самообмена допустима только для собственного exe, иначе можно переименовать чужой процесс.
        string exeName = Path.GetFileName(currentExe);
        if (!exeName.Equals("RevealLoader.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Самообновление доступно только для RevealLoader.exe.");

        string backupPath = GetBackupPath(currentExe);

        File.Move(currentExe, backupPath, overwrite: true);
        File.Move(downloadedPath, currentExe, overwrite: true);

        Process.Start(new ProcessStartInfo { FileName = currentExe, UseShellExecute = true });
        Environment.Exit(0);
    }

    /// <summary>Удаляет резервную копию прошлой версии, оставшуюся после обновления.</summary>
    public static void CleanupBackup()
    {
        try
        {
            string currentExe = Environment.ProcessPath ?? "";
            if (currentExe == "")
                return;

            string backupPath = GetBackupPath(currentExe);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
        catch
        {
            // Не удалось удалить копию — не критично.
        }
    }

    private static string GetBackupPath(string currentExe) =>
        Path.Combine(
            Path.GetDirectoryName(currentExe) ?? "",
            Path.GetFileNameWithoutExtension(currentExe) + ".old.exe");

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
