using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace RevealLoader;

/// <summary>Пакет визуала (самодостаточный клиент-jar) из манифеста.</summary>
public sealed record VisualPackage(string Version, string Url, string Sha256, long Size);

/// <summary>
/// Скачивает клиент-jar во временную (случайную) папку, запускает его с распаковкой
/// в %APPDATA%\.revealvisual и удаляет мод из game\mods после закрытия игры.
/// </summary>
public static class VisualService
{
    private const string AssetName = "RevealClient.jar";
    private const string ModRelativePath = @"game\mods\revealvisuals-1.21.11.jar";
    private const string VersionFileName = ".installed-version";
    private const string JarPathFileName = ".jar-path";

    /// <summary>Папка распаковки клиента: %APPDATA%\.revealvisual.</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".revealvisual");

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
    /// Гарантирует наличие нужной версии jar: качает в случайную папку во временной
    /// директории, если файла нет или вышла новая версия. Распаковка идёт в .revealvisual.
    /// Возвращает путь к jar, который нужно запустить.
    /// </summary>
    public static async Task<string> EnsureAsync(IProgress<double>? progress, CancellationToken token)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var package = await GetPackageAsync();
        if (package is null)
        {
            // Нет манифеста/ссылки — запускаем то, что уже скачано ранее.
            string? known = ReadState(JarPathFileName);
            if (known is not null && File.Exists(known))
                return known;
            throw new InvalidOperationException(
                "В манифесте нет ссылки на визуал, а клиент ещё не скачан.");
        }

        string installed = ReadState(VersionFileName) ?? "";
        string? previous = ReadState(JarPathFileName);
        bool upToDate = !string.IsNullOrEmpty(installed)
                        && string.Equals(installed, package.Version, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(previous)
                        && File.Exists(previous);
        if (upToDate)
            return previous!;

        string tempDir = Path.Combine(
            Path.GetTempPath(), "revealvisual-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(tempDir);
        string jarPath = Path.Combine(tempDir, AssetName);

        try
        {
            await UpdateService.DownloadToFileAsync(package.Url, package.Size, jarPath, progress, token);
            if (!UpdateService.VerifyHash(jarPath, package.Sha256))
                throw new InvalidOperationException("Проверка хеша не пройдена, файл повреждён.");
        }
        catch
        {
            TryDeleteFile(jarPath);
            throw;
        }

        // Убираем предыдущую скачанную копию, если она была.
        if (!string.IsNullOrEmpty(previous)
            && !string.Equals(previous, jarPath, StringComparison.OrdinalIgnoreCase))
            TryDeleteFile(previous);

        File.WriteAllText(Path.Combine(Directory, VersionFileName), package.Version);
        File.WriteAllText(Path.Combine(Directory, JarPathFileName), jarPath);
        return jarPath;
    }

    /// <summary>
    /// Запускает jar так же, как при двойном клике, задавая папку распаковки .revealvisual,
    /// и ждёт закрытия игры. После выхода игры удаляет мод из game\mods.
    /// </summary>
    public static async Task RunAsync(string jarPath, LauncherSettings settings)
    {
        if (!File.Exists(jarPath))
            throw new FileNotFoundException("Клиент не найден. Сначала скачайте визуал.");

        string java = string.IsNullOrWhiteSpace(settings.JavaPath) ? "javaw.exe" : settings.JavaPath;
        bool isBareName = string.Equals(java, "javaw.exe", StringComparison.OrdinalIgnoreCase);
        if (!isBareName && !File.Exists(java))
            throw new FileNotFoundException("Java не найдена. Укажите путь в настройках.");

        var startInfo = new ProcessStartInfo
        {
            FileName = java,
            Arguments = $"-Dreveal.baseDir=\"{Directory}\" -jar \"{jarPath}\"",
            WorkingDirectory = Directory,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo)
            ?? throw new Exception("Не удалось запустить клиент.");

        await process.WaitForExitAsync();
        DeleteMod();
    }

    private static string? ReadState(string fileName)
    {
        try
        {
            string path = Path.Combine(Directory, fileName);
            if (!File.Exists(path))
                return null;
            string value = File.ReadAllText(path).Trim();
            return value.Length == 0 ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Игнорируем: временный файл, не критично.
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
