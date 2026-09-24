using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>
/// Доступ к журналу файловых операций архивариуса (ARCHITECTURE §7.3, §8.6). Запись со статусом
/// <see cref="FileOperationLogStatus.Planned"/> создаётся до обращения к диску — отсюда crash-safety.
/// </summary>
public interface IFileOperationLogRepository
{
    Task AddAsync(FileOperationLogEntry entry, CancellationToken ct = default);

    Task UpdateAsync(FileOperationLogEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<FileOperationLogEntry>> GetByFileRecordAsync(Guid fileRecordId, CancellationToken ct = default);

    /// <summary>
    /// Записи, застрявшие в <see cref="FileOperationLogStatus.Planned"/> — след падения между записью
    /// журнала и файловой операцией. Их разбирает reconciliation (Phase 10).
    /// </summary>
    Task<IReadOnlyList<FileOperationLogEntry>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>Последние операции, свежие сверху — источник для будущего Undo (Phase 8).</summary>
    Task<IReadOnlyList<FileOperationLogEntry>> GetRecentAsync(int take, CancellationToken ct = default);
}

internal sealed class FileOperationLogRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IFileOperationLogRepository
{
    public async Task AddAsync(FileOperationLogEntry entry, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.FileOperationLogs.Add(entry);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(FileOperationLogEntry entry, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.FileOperationLogs.Update(entry);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileOperationLogEntry>> GetByFileRecordAsync(Guid fileRecordId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileOperationLogs
            .AsNoTracking()
            .Where(x => x.FileRecordId == fileRecordId)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileOperationLogEntry>> GetPendingAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileOperationLogs
            .AsNoTracking()
            .Where(x => x.Status == FileOperationLogStatus.Planned)
            .OrderBy(x => x.StartedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileOperationLogEntry>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileOperationLogs
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
