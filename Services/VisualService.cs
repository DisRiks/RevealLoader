using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace RevealLoader;

/// <summary>Пакет визуала (самодостаточный клиент-jar) из манифеста.</summary>
public sealed record VisualPackage(string Version, string Url, string Sha256, long Size);

/// <summary>
/// Скачивает клиент-jar в %APPDATA%\.revealvisual, запускает его как обычный jar
/// и удаляет мод из game\mods после закрытия игры.
/// </summary>
public static class VisualService
{
    private const string AssetName = "RevealClient.jar";
    private const string ModRelativePath = @"game\mods\revealvisuals-1.21.11.jar";
    private const string VersionFileName = ".installed-version";

    /// <summary>Папка визуала: %APPDATA%\.revealvisual.</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".revealvisual");

    /// <summary>Путь к скачанному клиент-jar.</summary>
    public static string JarPath => Path.Combine(Directory, AssetName);

    /// <summary>Путь к моду, который удаляется после закрытия игры.</summary>
    public static string ModPath => Path.Combine(Directory, ModRelativePath);

    /// <summary>
    /// Читает блок "visual" из манифеста. Возвращает null, если ссылки нет
    /// или сеть/манифест недоступны.
    /// </summary>
    public static async Task<VisualPackage?> GetPackageAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            string json = await http.GetStringAsync(UpdateService.ManifestUrl);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("visual", out var visual))
                return null;

            string url = visual.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString() ?? ""
                : "";
            // Без ссылки визуал скачать нельзя.
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string version = visual.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString() ?? ""
                : "";
            string sha = visual.TryGetProperty("sha256", out var shaElement)
                ? shaElement.GetString() ?? ""
                : "";
            long size = visual.TryGetProperty("size", out var sizeElement)
                        && sizeElement.TryGetInt64(out long parsedSize)
                ? parsedSize
                : 0;

            return new VisualPackage(version, url, sha, size);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Гарантирует наличие нужной версии jar: скачивает, если файла нет
    /// или вышла новая версия. Возвращает true, если файл был скачан.
    /// </summary>
    public static async Task<bool> EnsureAsync(IProgress<double>? progress, CancellationToken token)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var package = await GetPackageAsync();
        if (package is null)
        {
            // Нет манифеста/ссылки — запускаем то, что уже есть.
            if (File.Exists(JarPath))
                return false;
            throw new InvalidOperationException(
                "В манифесте нет ссылки на визуал, а клиент ещё не скачан.");
        }

        string installed = ReadInstalledVersion();
        bool needsDownload = !File.Exists(JarPath)
                             || string.IsNullOrEmpty(installed)
                             || !string.Equals(installed, package.Version, StringComparison.OrdinalIgnoreCase);
        if (!needsDownload)
            return false;

        string tempPath = JarPath + ".part";
        await UpdateService.DownloadToFileAsync(package.Url, package.Size, tempPath, progress, token);

        if (!UpdateService.VerifyHash(tempPath, package.Sha256))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Проверка хеша не пройдена, файл повреждён.");
        }

        File.Move(tempPath, JarPath, overwrite: true);
        File.WriteAllText(Path.Combine(Directory, VersionFileName), package.Version);
        return true;
    }

    /// <summary>
    /// Запускает скачанный jar так же, как при двойном клике, и ждёт закрытия игры.
    /// После выхода игры удаляет мод из game\mods.
    /// </summary>
    public static async Task RunAsync(LauncherSettings settings)
    {
        if (!File.Exists(JarPath))
            throw new FileNotFoundException("Клиент не найден. Сначала скачайте визуал.");

        string java = string.IsNullOrWhiteSpace(settings.JavaPath) ? "javaw.exe" : settings.JavaPath;
        bool isBareName = string.Equals(java, "javaw.exe", StringComparison.OrdinalIgnoreCase);
        if (!isBareName && !File.Exists(java))
            throw new FileNotFoundException("Java не найдена. Укажите путь в настройках.");

        var startInfo = new ProcessStartInfo
        {
            FileName = java,
            Arguments = $"-jar \"{JarPath}\"",
            WorkingDirectory = Directory,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo)
            ?? throw new Exception("Не удалось запустить клиент.");

        await process.WaitForExitAsync();
        DeleteMod();
    }

    private static string ReadInstalledVersion()
    {
        try
        {
            string path = Path.Combine(Directory, VersionFileName);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Удаляет мод из game\mods после закрытия игры, пережидая короткую блокировку файла.</summary>
    private static void DeleteMod()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (File.Exists(ModPath))
                    File.Delete(ModPath);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
