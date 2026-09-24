using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Учёт вложений дедлайна: материалы задания и файлы ответа.</summary>
public interface IDeadlineAttachmentRepository
{
    /// <summary>Вложения дедлайна в порядке добавления.</summary>
    Task<IReadOnlyList<DeadlineAttachment>> GetByDeadlineAsync(Guid deadlineId, CancellationToken ct = default);

    Task<DeadlineAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddManyAsync(IReadOnlyList<DeadlineAttachment> attachments, CancellationToken ct = default);

    /// <summary>Удаляет запись учёта; файл на диске не трогается (ARCHITECTURE §14).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Число вложений по каждому дедлайну одним запросом — для бейджей на карточках.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountsByDeadlineAsync(CancellationToken ct = default);
}

internal sealed class DeadlineAttachmentRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IDeadlineAttachmentRepository
{
    public async Task<IReadOnlyList<DeadlineAttachment>> GetByDeadlineAsync(Guid deadlineId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.DeadlineAttachments
            .AsNoTracking()
            .Where(x => x.DeadlineId == deadlineId)
            .OrderBy(x => x.AddedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<DeadlineAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.DeadlineAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddManyAsync(IReadOnlyList<DeadlineAttachment> attachments, CancellationToken ct = default)
    {
        if (attachments.Count == 0)
        {
            return;
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.DeadlineAttachments.AddRange(attachments);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.DeadlineAttachments
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountsByDeadlineAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        var rows = await context.DeadlineAttachments
            .AsNoTracking()
            .GroupBy(x => x.DeadlineId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ToDictionary(x => x.Key, x => x.Count);
    }
}
