using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Modules.Archivist.Services;

internal sealed class OperationHistoryService(
    IFileOperationLogRepository operationLog,
    IFileOperationExecutor executor,
    IFileSystem fileSystem) : IOperationHistoryService
{
    public async Task<IReadOnlyList<OperationHistoryItem>> GetRecentAsync(
        int take = 50, CancellationToken ct = default)
    {
        var entries = await operationLog
            .GetRecentAsync(Math.Clamp(take, 1, 200), ct)
            .ConfigureAwait(false);

        return entries
            .Select(entry => new OperationHistoryItem(
                FileRecordId: entry.FileRecordId,
                FileName: Path.GetFileName(entry.FinalPath ?? entry.PlannedPath),
                OriginalPath: entry.OriginalPath,
                FinalPath: entry.FinalPath,
                StartedAt: entry.StartedAt,
                Status: entry.Status,
                CanUndo: CanUndo(entry)))
            .ToList();
    }

    public async Task<Result> UndoAsync(Guid fileRecordId, CancellationToken ct = default)
    {
        var undone = await executor.UndoAsync(fileRecordId, ct).ConfigureAwait(false);
        return undone
            ? Result.Success()
            : Result.Failure(
                "archivist.undo_failed",
                "Отменить не получилось: файл уже перемещён вручную либо прежний путь снова занят.");
    }

    private bool CanUndo(FileOperationLogEntry entry) =>
        entry.Status == FileOperationLogStatus.Completed
        && entry.FinalPath is not null
        && fileSystem.FileExists(entry.FinalPath)
        && !fileSystem.FileExists(entry.OriginalPath);
}
