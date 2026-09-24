using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Исполнитель файловых операций (ARCHITECTURE §8.3, §8.4 п.5-6, §8.6, §14).
/// </summary>
/// <remarks>
/// Здесь сосредоточено самое строгое требование проекта: файл пользователя не удаляется и не
/// перезаписывается ни при каких условиях. Занятый целевой путь разрешается либо как дубликат
/// (файл остаётся на месте), либо суффиксом (2), (3)... Запись в журнал операций всегда делается
/// ДО обращения к диску - падение между журналом и операцией остаётся видимым.
/// </remarks>
internal sealed class FileOperationExecutor(
    IFileSystem fileSystem,
    IFileHasher hasher,
    IFileRecordRepository fileRecords,
    IFileOperationLogRepository operationLog,
    ILogger<FileOperationExecutor> logger) : IFileOperationExecutor
{
    private const int MaxSuffix = 999;

    /// <summary>
    /// Жёсткий предел длины компонента имени в NTFS/FAT — 255 символов, не зависит от того,
    /// включена ли в системе поддержка длинных путей. Имя длиннее физически не создать, поэтому
    /// такой файл сразу уходит в карантин, а не в бесконечный ретрай (ARCHITECTURE §8.8).
    /// </summary>
    private const int MaxFileNameLength = 255;

    public async Task<FileOperationResult> MoveAndRenameAsync(SortDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var record = await fileRecords.GetByIdAsync(decision.SourceFileRecordId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return new FileOperationResult(
                decision.SourceFileRecordId,
                FileOperationOutcome.Failed,
                null,
                new Error("archivist.record_not_found", "Запись учёта файла не найдена."));
        }

        var sourcePath = record.CurrentPath.Length > 0 ? record.CurrentPath : record.OriginalPath;

        if (!fileSystem.FileExists(sourcePath))
        {
            logger.LogInformation("Файл уже отсутствует по пути {Path} - операция отменена", sourcePath);
            return new FileOperationResult(
                record.Id,
                FileOperationOutcome.Failed,
                null,
                new Error("archivist.source_missing", "Исходный файл не найден."));
        }

        if (string.IsNullOrWhiteSpace(decision.TargetDirectory))
        {
            return new FileOperationResult(
                record.Id,
                FileOperationOutcome.Failed,
                null,
                new Error("archivist.target_not_set", "Целевая папка не задана."));
        }

        var plannedPath = Path.Combine(decision.TargetDirectory, decision.NewFileName);

        if (Path.GetFileName(plannedPath).Length > MaxFileNameLength)
        {
            logger.LogWarning(
                "Имя файла назначения длиннее {Limit} символов — файл {Path} оставлен на месте",
                MaxFileNameLength, sourcePath);
            return await FailAsync(
                record,
                new Error("archivist.path_too_long", "Слишком длинное имя файла назначения."),
                ct).ConfigureAwait(false);
        }

        if (PathsAreSame(sourcePath, plannedPath))
        {
            // Файл уже лежит там, где надо - трогать нечего.
            return new FileOperationResult(record.Id, FileOperationOutcome.Moved, sourcePath, null);
        }

        try
        {
            fileSystem.CreateDirectory(decision.TargetDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось создать целевую папку {Directory}", decision.TargetDirectory);
            return await FailAsync(
                record,
                new Error("archivist.target_unavailable", "Не удалось создать целевую папку."),
                ct).ConfigureAwait(false);
        }

        ConflictOutcome resolution;
        try
        {
            resolution = await ResolveConflictAsync(sourcePath, plannedPath, ct).ConfigureAwait(false);
        }
        catch (PathTooLongException ex)
        {
            logger.LogWarning(ex, "Путь назначения слишком длинный — файл {Path} оставлен на месте", sourcePath);
            return await FailAsync(
                record,
                new Error("archivist.path_too_long", "Слишком длинный путь назначения."),
                ct).ConfigureAwait(false);
        }

        if (resolution.IsDuplicate)
        {
            logger.LogInformation(
                "В целевой папке уже лежит файл с тем же содержимым - {Path} оставлен на месте", sourcePath);

            record.Status = FileRecordStatus.Quarantined;
            await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);

            return new FileOperationResult(
                record.Id, FileOperationOutcome.SkippedDuplicate, resolution.TargetPath, null);
        }

        if (resolution.TargetPath is null)
        {
            return await FailAsync(
                record,
                new Error("archivist.too_many_duplicates", "Слишком много файлов с таким именем в целевой папке."),
                ct).ConfigureAwait(false);
        }

        var finalPath = resolution.TargetPath;

        // Журнал пишется ДО файловой операции - иначе после падения не отличить "не начинали" от
        // "переместили, но не записали" (ARCHITECTURE §8.4 п.6).
        var logEntry = new FileOperationLogEntry
        {
            Id = Guid.NewGuid(),
            FileRecordId = record.Id,
            RuleId = decision.MatchedRule is { } rule && rule.Id != Guid.Empty ? rule.Id : null,
            OriginalPath = sourcePath,
            PlannedPath = finalPath,
            StartedAt = DateTimeOffset.Now,
            Status = FileOperationLogStatus.Planned,
        };
        await operationLog.AddAsync(logEntry, ct).ConfigureAwait(false);

        try
        {
            // overwrite: false - единственный допустимый режим (ARCHITECTURE §8.6, §14).
            fileSystem.Move(sourcePath, finalPath, overwrite: false);
        }
        catch (PathTooLongException ex)
        {
            // PathTooLongException - наследник IOException, но ретрай тут бессмыслен: путь не станет
            // короче сам. Ловим отдельно ДО общего фильтра и уводим в карантин (ARCHITECTURE §8.8).
            logger.LogWarning(ex, "Путь назначения слишком длинный - файл {Source} оставлен на месте", sourcePath);

            logEntry.Status = FileOperationLogStatus.Failed;
            logEntry.CompletedAt = DateTimeOffset.Now;
            await operationLog.UpdateAsync(logEntry, ct).ConfigureAwait(false);

            record.Status = FileRecordStatus.Quarantined;
            await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);

            return new FileOperationResult(
                record.Id,
                FileOperationOutcome.Failed,
                null,
                new Error("archivist.path_too_long", "Слишком длинный путь назначения."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось переместить файл {Source} - оставлен на месте", sourcePath);

            logEntry.Status = FileOperationLogStatus.Failed;
            logEntry.CompletedAt = DateTimeOffset.Now;
            await operationLog.UpdateAsync(logEntry, ct).ConfigureAwait(false);

            // Deferred, а не Quarantined: наблюдатель повторит попытку по бэкоффу (ARCHITECTURE §8.6).
            record.Status = FileRecordStatus.Deferred;
            await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);

            return new FileOperationResult(
                record.Id,
                FileOperationOutcome.Deferred,
                null,
                new Error("archivist.file_locked", "Файл занят другим процессом, попробуем позже."));
        }

        logEntry.Status = FileOperationLogStatus.Completed;
        logEntry.FinalPath = finalPath;
        logEntry.CompletedAt = DateTimeOffset.Now;
        await operationLog.UpdateAsync(logEntry, ct).ConfigureAwait(false);

        record.CurrentPath = finalPath;
        record.SubjectId = decision.SubjectId;
        record.Status = FileRecordStatus.Sorted;
        await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);

        logger.LogInformation("Файл отсортирован: {Source} -> {Target}", sourcePath, finalPath);
        return new FileOperationResult(record.Id, FileOperationOutcome.Moved, finalPath, null);
    }

    /// <summary>
    /// Отмена последней успешной сортировки файла (ARCHITECTURE §8.6): файл возвращается на
    /// <c>OriginalPath</c> через <c>Move(overwrite: false)</c>. Возвращает <see langword="false"/>
    /// и ничего не трогает, если возвращать нечего (файл уже унесли) либо прежний путь снова занят —
    /// перезаписывать чужой файл нельзя ни при каких условиях (§14).
    /// </summary>
    public async Task<bool> UndoAsync(Guid fileRecordId, CancellationToken ct)
    {
        var record = await fileRecords.GetByIdAsync(fileRecordId, ct).ConfigureAwait(false);
        if (record is null)
        {
            logger.LogInformation("Отмена сортировки: запись {FileRecordId} не найдена", fileRecordId);
            return false;
        }

        var entries = await operationLog.GetByFileRecordAsync(fileRecordId, ct).ConfigureAwait(false);
        var lastCompleted = entries.FirstOrDefault(
            e => e.Status == FileOperationLogStatus.Completed && e.FinalPath is not null);
        if (lastCompleted?.FinalPath is null)
        {
            logger.LogInformation(
                "Отмена сортировки: для записи {FileRecordId} нет завершённой операции", fileRecordId);
            return false;
        }

        var currentPath = lastCompleted.FinalPath;
        var originalPath = lastCompleted.OriginalPath;

        if (!fileSystem.FileExists(currentPath))
        {
            logger.LogInformation(
                "Отмена сортировки: файл {Path} больше не на месте — возвращать нечего", currentPath);
            return false;
        }

        if (fileSystem.FileExists(originalPath))
        {
            logger.LogInformation(
                "Отмена сортировки: прежний путь {Path} снова занят — перезапись запрещена (§14)", originalPath);
            return false;
        }

        try
        {
            var originalDirectory = Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrEmpty(originalDirectory))
            {
                fileSystem.CreateDirectory(originalDirectory);
            }

            fileSystem.Move(currentPath, originalPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // PathTooLongException (наследник IOException) сюда же: возвращать нечего — просто отказ.
            logger.LogWarning(ex, "Отмену сортировки {FileRecordId} выполнить не удалось", fileRecordId);
            return false;
        }

        lastCompleted.Status = FileOperationLogStatus.RolledBack;
        lastCompleted.CompletedAt = DateTimeOffset.Now;
        await operationLog.UpdateAsync(lastCompleted, ct).ConfigureAwait(false);

        record.CurrentPath = originalPath;
        record.SubjectId = null;
        record.Status = FileRecordStatus.UndoneByUser;
        await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);

        logger.LogInformation("Сортировка отменена: {From} -> {To}", currentPath, originalPath);
        return true;
    }

    /// <summary>
    /// Разрешение конфликта имён (ARCHITECTURE §8.4 п.5): совпал хэш - дубликат, различается -
    /// подбираем свободный суффикс. Существующий файл не трогаем ни в одной из веток.
    /// </summary>
    private async Task<ConflictOutcome> ResolveConflictAsync(
        string sourcePath,
        string plannedPath,
        CancellationToken ct)
    {
        if (!fileSystem.FileExists(plannedPath))
        {
            return new ConflictOutcome(plannedPath, IsDuplicate: false);
        }

        var sourceHash = await hasher.ComputeAsync(sourcePath, ct).ConfigureAwait(false);
        var targetHash = await hasher.ComputeAsync(plannedPath, ct).ConfigureAwait(false);

        if (string.Equals(sourceHash, targetHash, StringComparison.Ordinal))
        {
            return new ConflictOutcome(plannedPath, IsDuplicate: true);
        }

        var directory = Path.GetDirectoryName(plannedPath)!;
        var stem = Path.GetFileNameWithoutExtension(plannedPath);
        var extension = Path.GetExtension(plannedPath);

        for (var suffix = 2; suffix <= MaxSuffix; suffix++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!fileSystem.FileExists(candidate))
            {
                return new ConflictOutcome(candidate, IsDuplicate: false);
            }

            var candidateHash = await hasher.ComputeAsync(candidate, ct).ConfigureAwait(false);
            if (string.Equals(sourceHash, candidateHash, StringComparison.Ordinal))
            {
                return new ConflictOutcome(candidate, IsDuplicate: true);
            }
        }

        return new ConflictOutcome(null, IsDuplicate: false);
    }

    private async Task<FileOperationResult> FailAsync(FileRecord record, Error error, CancellationToken ct)
    {
        record.Status = FileRecordStatus.Quarantined;
        await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);
        return new FileOperationResult(record.Id, FileOperationOutcome.Failed, null, error);
    }

    private static bool PathsAreSame(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private readonly record struct ConflictOutcome(string? TargetPath, bool IsDuplicate);
}
