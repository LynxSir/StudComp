namespace StudComp.Infrastructure.Backup;

/// <summary>
/// Метаданные архива резервной копии (кладётся в <c>manifest.json</c>). Нужен для диагностики,
/// проверки целостности и (с Phase 13.8) идентификации линии копий при восстановлении —
/// <see cref="LineageId"/>/<see cref="Version"/> хвостовые опциональные параметры: старые архивы
/// без этих полей десериализуются в <see cref="Guid.Empty"/>/<c>0</c> — тот самый сентинел
/// «неизвестная линия» (new_addons.md §13.2), а не ошибка разбора.
/// </summary>
public sealed record BackupManifest(
    int SchemaVersion,
    string AppVersion,
    DateTimeOffset CreatedUtc,
    string DatabaseSha256,
    IReadOnlyList<string> Files,
    Guid LineageId = default,
    int Version = 0,
    IReadOnlyList<BackupResource>? Resources = null,
    IReadOnlyDictionary<string, string>? FileHashes = null)
{
    /// <summary>Версия формата архива.</summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>Имя файла БД внутри архива.</summary>
    public const string DatabaseEntryName = "rubrica.db";

    /// <summary>Имя файла настроек внутри архива.</summary>
    public const string SettingsEntryName = "usersettings.json";

    /// <summary>Имя файла манифеста внутри архива.</summary>
    public const string ManifestEntryName = "manifest.json";
}

public sealed record BackupResource(string SourcePath, string ArchivePath, bool IsDirectory);
