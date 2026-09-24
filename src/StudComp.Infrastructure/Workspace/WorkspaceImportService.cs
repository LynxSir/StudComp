using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Common;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Infrastructure.Workspace;

/// <inheritdoc cref="IWorkspaceImportService"/>
/// <remarks>
/// Алгоритм разрешения конфликтов намеренно повторяет <c>FileOperationExecutor</c> Архивариуса
/// (ARCHITECTURE §8.4 п.5): совпал хэш — дубликат, различается — свободный суффикс. Существующий
/// файл не трогается ни в одной ветке, исходник в режиме <see cref="ImportMode.Copy"/> — тоже.
/// </remarks>
internal sealed class WorkspaceImportService(
    IFileSystem fileSystem,
    IFileHasher hasher,
    ILogger<WorkspaceImportService> logger) : IWorkspaceImportService
{
    private const int MaxSuffix = 999;

    public async Task<ImportSummary> ImportAsync(
        IReadOnlyList<string> sourcePaths,
        string targetDirectory,
        ImportMode mode,
        DuplicatePolicy duplicates = DuplicatePolicy.Skip,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        Guard.NotNullOrWhiteSpace(targetDirectory);

        var plan = BuildPlan(sourcePaths, targetDirectory);
        var results = new List<ImportItemResult>(plan.Count);
        var cancelled = false;

        for (var i = 0; i < plan.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var item = plan[i];
            progress?.Report(new ImportProgress(i, plan.Count, Path.GetFileName(item.SourcePath)));

            // Токен внутрь намеренно не передаётся: отмена проверяется только на границе файлов,
            // а начатая операция доводится до конца. Иначе прерванное чтение/копирование оставило бы
            // обрывок в папке предмета, а удалить его нечем и, по ADR §16.30, нечем и не следует.
            results.Add(await ImportOneAsync(item, mode, duplicates, CancellationToken.None)
                .ConfigureAwait(false));
        }

        if (!cancelled)
        {
            progress?.Report(new ImportProgress(plan.Count, plan.Count, string.Empty));
        }

        logger.LogInformation(
            "Импорт в {Target}: перенесено {Imported}, пропущено дублей {Skipped}, ошибок {Failed}{Cancelled}",
            targetDirectory,
            results.Count(x => x.Outcome is ImportItemOutcome.Imported or ImportItemOutcome.RenamedDueToConflict),
            results.Count(x => x.Outcome == ImportItemOutcome.SkippedDuplicate),
            results.Count(x => x.Outcome == ImportItemOutcome.Failed),
            cancelled ? " (отменено пользователем)" : string.Empty);

        return new ImportSummary(results, cancelled);
    }

    /// <summary>
    /// Разворачивает список источников в плоский список «файл → целевая папка»: перетащенная папка
    /// воспроизводится внутри цели вместе со структурой подпапок.
    /// </summary>
    private List<PlannedItem> BuildPlan(IReadOnlyList<string> sourcePaths, string targetDirectory)
    {
        var plan = new List<PlannedItem>();

        foreach (var path in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (fileSystem.DirectoryExists(path))
            {
                var folderName = Sanitize(Path.GetFileName(path.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                AddDirectory(plan, path, Path.Combine(targetDirectory, folderName));
            }
            else
            {
                // Несуществующий путь тоже кладём в план: пользователь должен увидеть в отчёте, что
                // файл исчез между перетаскиванием и импортом, а не недосчитаться строки молча.
                plan.Add(new PlannedItem(path, targetDirectory));
            }
        }

        return plan;
    }

    private void AddDirectory(List<PlannedItem> plan, string sourceDirectory, string targetDirectory)
    {
        foreach (var file in fileSystem.EnumerateFiles(sourceDirectory))
        {
            plan.Add(new PlannedItem(file, targetDirectory));
        }

        foreach (var directory in fileSystem.EnumerateDirectories(sourceDirectory))
        {
            var name = Sanitize(Path.GetFileName(directory));
            AddDirectory(plan, directory, Path.Combine(targetDirectory, name));
        }
    }

    private async Task<ImportItemResult> ImportOneAsync(
        PlannedItem item, ImportMode mode, DuplicatePolicy duplicates, CancellationToken ct)
    {
        var source = item.SourcePath;

        if (!fileSystem.FileExists(source))
        {
            return Fail(source, "workspace.source_missing", "Исходный файл не найден.");
        }

        // Проверки длины имени, как у Архивариуса (§8.8), здесь нет и не нужно: имя берётся у уже
        // существующего файла, то есть заведомо создаваемое. Слишком длинным может оказаться только
        // путь целиком — это ловится PathTooLongException ниже.
        var plannedPath = Path.Combine(item.TargetDirectory, Path.GetFileName(source));

        if (PathsAreSame(source, plannedPath))
        {
            // Файл уже лежит там, куда его тащат — трогать нечего.
            return new ImportItemResult(source, plannedPath, ImportItemOutcome.Imported, null, null);
        }

        try
        {
            fileSystem.CreateDirectory(item.TargetDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось создать папку {Directory}", item.TargetDirectory);
            return Fail(source, "workspace.target_unavailable", "Не удалось создать целевую папку.");
        }

        string sourceHash;
        ConflictOutcome resolution;
        try
        {
            sourceHash = await hasher.ComputeAsync(source, ct).ConfigureAwait(false);
            resolution = await ResolveConflictAsync(sourceHash, plannedPath, ct).ConfigureAwait(false);
        }
        catch (PathTooLongException ex)
        {
            logger.LogWarning(ex, "Слишком длинный путь назначения для {Source}", source);
            return Fail(source, "workspace.path_too_long", "Слишком длинный путь назначения.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Файл {Source} недоступен для чтения", source);
            return Fail(source, "workspace.file_locked", "Файл занят другим процессом.");
        }

        if (resolution.IsDuplicate && duplicates == DuplicatePolicy.Skip)
        {
            logger.LogInformation(
                "В папке предмета уже лежит файл с тем же содержимым — {Source} оставлен на месте", source);
            return new ImportItemResult(
                source, resolution.TargetPath, ImportItemOutcome.SkippedDuplicate, sourceHash, null);
        }

        if (resolution.TargetPath is null || (resolution.IsDuplicate && resolution.FreePath is null))
        {
            return Fail(source, "workspace.too_many_duplicates", "Слишком много файлов с таким именем.");
        }

        // При KeepBoth дубликат кладём рядом свободным именем, а не поверх найденного совпадения.
        var finalPath = resolution.IsDuplicate ? resolution.FreePath! : resolution.TargetPath;

        try
        {
            // overwrite: false в обеих ветках — единственный допустимый режим (§14).
            if (mode == ImportMode.Move)
            {
                fileSystem.Move(source, finalPath, overwrite: false);
            }
            else
            {
                fileSystem.Copy(source, finalPath, overwrite: false);
            }
        }
        catch (PathTooLongException ex)
        {
            logger.LogWarning(ex, "Слишком длинный путь назначения для {Source}", source);
            return Fail(source, "workspace.path_too_long", "Слишком длинный путь назначения.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось импортировать {Source} — файл оставлен на месте", source);
            return Fail(source, "workspace.file_locked", "Файл занят другим процессом или недоступен.");
        }

        var outcome = PathsAreSame(finalPath, plannedPath)
            ? ImportItemOutcome.Imported
            : ImportItemOutcome.RenamedDueToConflict;

        logger.LogInformation("Импортирован файл: {Source} -> {Target}", source, finalPath);
        return new ImportItemResult(source, finalPath, outcome, sourceHash, null);
    }

    /// <summary>
    /// Разрешение конфликта имён: совпал хэш — дубликат, различается — подбираем свободный суффикс.
    /// Существующий файл не трогаем ни в одной из веток (ARCHITECTURE §8.4 п.5, §14).
    /// </summary>
    private async Task<ConflictOutcome> ResolveConflictAsync(
        string sourceHash, string plannedPath, CancellationToken ct)
    {
        if (!fileSystem.FileExists(plannedPath))
        {
            return new ConflictOutcome(plannedPath, IsDuplicate: false, FreePath: plannedPath);
        }

        var duplicateFound = string.Equals(
            sourceHash, await hasher.ComputeAsync(plannedPath, ct).ConfigureAwait(false), StringComparison.Ordinal);

        var directory = Path.GetDirectoryName(plannedPath)!;
        var stem = Path.GetFileNameWithoutExtension(plannedPath);
        var extension = Path.GetExtension(plannedPath);

        for (var suffix = 2; suffix <= MaxSuffix; suffix++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!fileSystem.FileExists(candidate))
            {
                return new ConflictOutcome(candidate, duplicateFound, candidate);
            }

            if (!duplicateFound && string.Equals(
                    sourceHash,
                    await hasher.ComputeAsync(candidate, ct).ConfigureAwait(false),
                    StringComparison.Ordinal))
            {
                return new ConflictOutcome(candidate, IsDuplicate: true, FreePath: null);
            }
        }

        return new ConflictOutcome(null, duplicateFound, null);
    }

    private static ImportItemResult Fail(string source, string code, string message) =>
        new(source, null, ImportItemOutcome.Failed, null, new Error(code, message));

    private static bool PathsAreSame(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Без названия";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. trimmed.Select(ch => invalid.Contains(ch) ? '_' : ch)]);
        return cleaned.TrimEnd('.', ' ') is { Length: > 0 } result ? result : "Без названия";
    }

    private readonly record struct PlannedItem(string SourcePath, string TargetDirectory);

    /// <param name="TargetPath">Найденное место (для дубля — путь совпавшего файла).</param>
    /// <param name="IsDuplicate">В целевой папке уже есть файл с тем же содержимым.</param>
    /// <param name="FreePath">Свободное имя, если импортировать всё-таки нужно.</param>
    private readonly record struct ConflictOutcome(string? TargetPath, bool IsDuplicate, string? FreePath);
}
