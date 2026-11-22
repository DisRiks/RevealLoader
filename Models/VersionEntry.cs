namespace RevealLoader;

/// <summary>Одна запись в истории версий: какая версия и что в неё добавили.</summary>
public sealed class VersionEntry
{
    /// <summary>Версия, которой соответствует запись, например "1.1.0".</summary>
    public string Version { get; set; } = "";

    /// <summary>Тип обновления: "global" (глобальное) или "normal" (обычное).</summary>
    public string Type { get; set; } = "normal";

    /// <summary>Список изменений на русском.</summary>
    public List<string> Ru { get; set; } = new();

    /// <summary>Список изменений на английском.</summary>
    public List<string> En { get; set; } = new();

    /// <summary>Глобальное ли это обновление.</summary>
    public bool IsGlobal => string.Equals(Type, "global", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Корневой объект вшитого в лаунчер файла Resources/history.json.</summary>
public sealed class VersionHistory
{
    public List<VersionEntry> Entries { get; set; } = new();
}
