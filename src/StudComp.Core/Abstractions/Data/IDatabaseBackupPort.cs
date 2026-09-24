namespace StudComp.Core.Abstractions.Data;

/// <summary>
/// Работа с файлом БД в обход EF/провайдера — для резервного копирования и восстановления
/// (new_addons.md §7 §9). Живёт в <c>Core</c> (BCL-only сигнатуры), реализация с
/// <c>Microsoft.Data.Sqlite</c> — в <c>StudComp.Data</c>; так <c>Infrastructure</c> не тянет EF/Sqlite
/// (та же логика, что ADR §16.16 и раскол <c>IStudyWorkspace</c>).
/// </summary>
public interface IDatabaseBackupPort
{
    Task<IReadOnlyList<string>> GetResourcePathsAsync(string snapshotPath, CancellationToken ct = default, string? studyRootPath = null) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    Task RemapResourcePathsAsync(string snapshotPath, IReadOnlyList<BackupPathMapping> mappings, CancellationToken ct = default, string? sourceStudyRoot = null) =>
        Task.CompletedTask;

    /// <summary>
    /// Пишет консистентный снимок рабочей БД в отдельный файл, не останавливая приложение
    /// (SQLite Online Backup API). Каталог назначения создаётся при необходимости.
    /// </summary>
    Task WriteSnapshotAsync(string destinationFilePath, CancellationToken ct = default);

    /// <summary>
    /// Закрывает пулы соединений и заменяет рабочую БД содержимым указанного файла, удаляя
    /// возможные sidecar-файлы (<c>-wal</c>/<c>-shm</c>/<c>-journal</c>). После вызова приложение
    /// нужно перезапустить.
    /// </summary>
    /// <param name="sourceFilePath">Восстановленный файл БД (staging), которым заменяем рабочий.</param>
    /// <param name="destinationFilePath">
    /// Куда заменять; <see langword="null"/> — рабочая БД приложения (<c>RubricaPaths.DatabaseFile</c>).
    /// Явный путь нужен тестам Phase 13.8 (сквозной round-trip на temp-файлах, не на реальной БД
    /// пользователя) — в проде параметр не передаётся, поведение не меняется.
    /// </param>
    Task ReplaceDatabaseAsync(string sourceFilePath, string? destinationFilePath = null, CancellationToken ct = default);
}

public sealed record BackupPathMapping(string SourcePath, string DestinationPath, bool IsDirectory)
{
    public static string Remap(string path, IReadOnlyList<BackupPathMapping> mappings)
    {
        var normalized = path.Replace('/', '\\');
        foreach (var mapping in mappings.OrderByDescending(x => x.SourcePath.Length))
        {
            var source = mapping.SourcePath.Replace('/', '\\').TrimEnd('\\');
            if (normalized.Equals(source, StringComparison.OrdinalIgnoreCase)) return mapping.DestinationPath;
            if (mapping.IsDirectory && normalized.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(mapping.DestinationPath, normalized[(source.Length + 1)..]);
        }
        return path;
    }
}
