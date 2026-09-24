using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к ленте активности (ARCHITECTURE §7.3, new_addons.md §2). Источник для Дашборда.</summary>
public interface IActivityRepository
{
    Task AddAsync(ActivityEntry entry, CancellationToken ct = default);

    /// <summary>Последние записи, свежие сверху.</summary>
    Task<IReadOnlyList<ActivityEntry>> GetRecentAsync(int take, CancellationToken ct = default);

    /// <summary>Последние записи заданных видов, свежие сверху.</summary>
    Task<IReadOnlyList<ActivityEntry>> GetRecentByKindsAsync(
        int take,
        IReadOnlyCollection<ActivityKind> kinds,
        CancellationToken ct = default);

    /// <summary>
    /// Ретеншн: удаляет записи старше <paramref name="maxAge"/> и всё за пределами свежих
    /// <paramref name="keep"/> по времени. Возвращает число удалённых строк.
    /// </summary>
    Task<int> PruneAsync(int keep, TimeSpan maxAge, CancellationToken ct = default);
}

internal sealed class ActivityRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IActivityRepository
{
    public async Task AddAsync(ActivityEntry entry, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ActivityLog.Add(entry);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActivityEntry>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ActivityLog
            .AsNoTracking()
            .OrderByDescending(x => x.Timestamp)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActivityEntry>> GetRecentByKindsAsync(
        int take,
        IReadOnlyCollection<ActivityKind> kinds,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ActivityLog
            .AsNoTracking()
            .Where(x => kinds.Contains(x.Kind))
            .OrderByDescending(x => x.Timestamp)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> PruneAsync(int keep, TimeSpan maxAge, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var deleted = await context.ActivityLog
            .Where(x => x.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        // Оставшееся сверх свежих keep — по времени.
        var survivors = await context.ActivityLog
            .AsNoTracking()
            .OrderByDescending(x => x.Timestamp)
            .Skip(keep)
            .Select(x => x.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (survivors.Count > 0)
        {
            deleted += await context.ActivityLog
                .Where(x => survivors.Contains(x.Id))
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
        }

        return deleted;
    }
}
