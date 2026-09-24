using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Учёт файлов, импортированных в папку предмета (new_addons.md §1.11): запись в
/// <c>FileRecord</c> и в ленту активности.
/// </summary>
/// <remarks>
/// Сам импорт делает <see cref="IWorkspaceImportService"/> в инфраструктуре, но записать результат в
/// БД оттуда нельзя: <c>Infrastructure</c> не ссылается на <c>Data</c> (ARCHITECTURE §5.1). Поэтому
/// файловая часть и учёт разведены, и здесь — только вторая. Прецедент похода Органайзера в
/// <see cref="IFileRecordRepository"/> уже есть — привязка файла к дедлайну (§9.3, Phase 10).
/// </remarks>
public interface IWorkspaceFileLedger
{
    /// <summary>Занести в учёт удачно импортированные файлы. Неудачи и дубликаты игнорируются.</summary>
    Task RecordImportAsync(Guid subjectId, ImportSummary summary, CancellationToken ct = default);
}

internal sealed class WorkspaceFileLedger(
    IFileRecordRepository fileRecords,
    IActivityRepository activity,
    ILogger<WorkspaceFileLedger> logger) : IWorkspaceFileLedger
{
    public async Task RecordImportAsync(
        Guid subjectId, ImportSummary summary, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(summary);

        foreach (var item in summary.Items)
        {
            if (item.Outcome is not (ImportItemOutcome.Imported or ImportItemOutcome.RenamedDueToConflict)
                || item.FinalPath is not { } finalPath)
            {
                continue;
            }

            try
            {
                await UpsertRecordAsync(subjectId, item, finalPath, ct).ConfigureAwait(false);
                await WriteActivityAsync(subjectId, finalPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Файл уже на месте — сбой учёта не должен выглядеть как сбой импорта.
                logger.LogWarning(ex, "Не удалось занести в учёт импортированный файл {Path}", finalPath);
            }
        }
    }

    private async Task UpsertRecordAsync(
        Guid subjectId, ImportItemResult item, string finalPath, CancellationToken ct)
    {
        var existing = await fileRecords
            .GetByOriginalPathAsync(item.SourcePath, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.CurrentPath = finalPath;
            existing.SubjectId = subjectId;
            existing.Status = FileRecordStatus.Sorted;
            if (item.ContentHash is { Length: > 0 } hash)
            {
                existing.ContentHash = hash;
            }

            await fileRecords.UpdateAsync(existing, ct).ConfigureAwait(false);
            return;
        }

        // При копировании оригинал остаётся вне учебной папки и нашим не считается: за OriginalPath
        // берём итоговый путь, иначе Undo Архивариуса однажды попробовал бы «вернуть» файл наружу.
        var isCopy = File.Exists(item.SourcePath);

        await fileRecords.AddAsync(
            new FileRecord
            {
                Id = Guid.NewGuid(),
                SubjectId = subjectId,
                OriginalPath = isCopy ? finalPath : item.SourcePath,
                CurrentPath = finalPath,
                ContentHash = item.ContentHash ?? string.Empty,
                DetectedAt = DateTimeOffset.Now,
                Status = FileRecordStatus.Sorted,
            },
            ct).ConfigureAwait(false);
    }

    private Task WriteActivityAsync(Guid subjectId, string finalPath, CancellationToken ct) =>
        activity.AddAsync(
            new ActivityEntry
            {
                Id = Guid.NewGuid(),
                Kind = ActivityKind.FileImported,
                Timestamp = DateTimeOffset.Now,
                SubjectId = subjectId,
                Path = finalPath,
                Title = Path.GetFileName(finalPath),
            },
            ct);
}
