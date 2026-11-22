using System.Diagnostics;
using System.IO;

namespace RevealLoader;

public class MinecraftLauncher
{
    private readonly LauncherSettings _settings;
    private readonly string _gameDir;

    public MinecraftLauncher(LauncherSettings settings)
    {
        _settings = settings;
        _gameDir = Path.Combine(Path.GetTempPath(), "PerritoDLC");
    }

    public async Task LaunchAsync()
    {
        if (string.IsNullOrEmpty(_settings.JavaPath) || !File.Exists(_settings.JavaPath))
            throw new FileNotFoundException("Java не найдена. Укажите путь в настройках.");

        string jarPath = LocateGameJar();

        string version = _settings.SelectedVersion;
        string versionId = version == "1.21.4" ? "1.21.4" : "PerritoDLC";
        string launchMainClass = version == "1.21.4"
            ? "net.minecraft.client.main.Main"
            : "PerritoLauncher";
        string assetIndex = version == "1.21.4" ? "1.21" : "1.16";

        string versionDir = Path.Combine(_gameDir, "versions", versionId);
        Directory.CreateDirectory(versionDir);

        string nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);

        string args = BuildJvmArgs(jarPath, nativesDir, launchMainClass, versionId, assetIndex, _gameDir);

        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.JavaPath,
            Arguments = args,
            WorkingDirectory = _gameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo)
            ?? throw new Exception("Не удалось запустить Java");

        string stderr = await process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(stderr))
            throw new Exception($"Java (code {process.ExitCode}): {stderr.Substring(0, Math.Min(stderr.Length, 500))}");
    }

    /// <summary>Ищет файл игры рядом с лаунчером.</summary>
    private static string LocateGameJar()
    {
        string jarPath = Path.Combine(AppContext.BaseDirectory, "PerritoDLC.jar");
        if (!File.Exists(jarPath))
            throw new FileNotFoundException("Не найден PerritoDLC.jar рядом с лаунчером.");

        return jarPath;
    }

    private string BuildJvmArgs(string versionJar, string nativesDir, string mainClass, string versionId, string assetIndex, string versionDir)
    {
        int minMb = _settings.RamMinMb;
        int maxMb = _settings.RamMaxMb;

        return $"-Xmx{maxMb}M " +
               $"-Xms{minMb}M " +
               $"-Djava.library.path=\"{nativesDir}\" " +
               $"-cp \"{versionJar}\" " +
               $"{mainClass} " +
               $"--username {_settings.Nick} " +
               $"--version {versionId} " +
               $"--gameDir \"{versionDir}\" " +
               $"--assetsDir \"{versionDir}\" " +
               $"--assetIndex {assetIndex} " +
               $"--accessToken 0 " +
               $"--uuid 0";
    }
}
