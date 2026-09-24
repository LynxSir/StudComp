using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Раздел «Неразобранное» (ARCHITECTURE §8.4 п.7): файлы, для которых не нашлось правила либо операция
/// была отложена. Ручная сортировка идёт через тот же <see cref="IFileOperationExecutor"/>, что и
/// автоматическая - гарантии §14 одни и те же.
/// </summary>
public interface IUnsortedFileService
{
    /// <summary>Записи, которые ждут разбора и файлы которых ещё на месте.</summary>
    Task<IReadOnlyList<FileRecord>> GetUnsortedAsync(CancellationToken ct = default);

    /// <summary>
    /// Разложить файл руками: пользователь выбирает предмет, имя остаётся исходным - шаблоны здесь не
    /// применяются, чтобы результат ручного действия был предсказуем.
    /// </summary>
    Task<Result> SortManuallyAsync(Guid fileRecordId, Guid subjectId, CancellationToken ct = default);

    /// <summary>Убрать запись из списка, файл при этом не трогаем вообще.</summary>
    Task<Result> ForgetAsync(Guid fileRecordId, CancellationToken ct = default);
}

internal sealed class UnsortedFileService(
    IFileRecordRepository fileRecords,
    ISubjectRepository subjects,
    IFileOperationExecutor executor,
    IFileSystem fileSystem,
    SubjectTargetResolver targets,
    ILogger<UnsortedFileService> logger) : IUnsortedFileService
{
    private static readonly FileRecordStatus[] PendingStatuses =
        [FileRecordStatus.Detected, FileRecordStatus.Quarantined, FileRecordStatus.Deferred];

    public async Task<IReadOnlyList<FileRecord>> GetUnsortedAsync(CancellationToken ct = default)
    {
        var records = await fileRecords.GetByStatusesAsync(PendingStatuses, ct).ConfigureAwait(false);

        // Файл могли унести руками - показывать «призраков» бессмысленно.
        return records
            .Where(r => fileSystem.FileExists(PathOf(r)))
            .ToList();
    }

    public async Task<Result> SortManuallyAsync(Guid fileRecordId, Guid subjectId, CancellationToken ct = default)
    {
        var record = await fileRecords.GetByIdAsync(fileRecordId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return Result.Failure("archivist.record_not_found", "Запись учёта файла не найдена.");
        }

        var subject = await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return Result.Failure("archivist.subject_not_found", "Предмет не найден.");
        }

        var targetDirectory = targets.Resolve(subject);
        if (targetDirectory is null)
        {
            return Result.Failure(
                "archivist.target_not_set",
                "У предмета не задана папка, а корень архива не выбран в настройках.");
        }

        var decision = new SortDecision(
            SourceFileRecordId: record.Id,
            SubjectId: subject.Id,
            TargetDirectory: targetDirectory,
            NewFileName: Path.GetFileName(PathOf(record)),
            MatchedRule: ManualRule,
            Conflict: ConflictResolution.None);

        var result = await executor.MoveAndRenameAsync(decision, ct).ConfigureAwait(false);

        return result.Outcome switch
        {
            FileOperationOutcome.Moved => Result.Success(),
            FileOperationOutcome.SkippedDuplicate => Result.Failure(
                "archivist.duplicate",
                "В папке предмета уже лежит такой же файл - исходный оставлен на месте."),
            _ => Result.Failure(result.Error ?? new Error("archivist.move_failed", "Не удалось переместить файл.")),
        };
    }

    public async Task<Result> ForgetAsync(Guid fileRecordId, CancellationToken ct = default)
    {
        var record = await fileRecords.GetByIdAsync(fileRecordId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return Result.Failure("archivist.record_not_found", "Запись учёта файла не найдена.");
        }

        // Файл остаётся на диске нетронутым - меняется только его статус в учёте (ARCHITECTURE §14).
        record.Status = FileRecordStatus.UndoneByUser;
        await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);
        logger.LogInformation("Запись {Id} убрана из «Неразобранного», файл не тронут", record.Id);
        return Result.Success();
    }

    private static string PathOf(FileRecord record) =>
        record.CurrentPath.Length > 0 ? record.CurrentPath : record.OriginalPath;

    /// <summary>Псевдоправило для ручной сортировки: журнал операций пишется без ссылки на правило.</summary>
    private static ArchivistRule ManualRule { get; } = new()
    {
        Id = Guid.Empty,
        Pattern = string.Empty,
        MatchType = RuleMatchType.Keyword,
        RenameTemplate = string.Empty,
        Enabled = false,
    };
}
