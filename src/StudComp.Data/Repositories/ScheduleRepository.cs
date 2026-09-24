using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к записям расписания (ARCHITECTURE §7.3, §9.2).</summary>
public interface IScheduleRepository
{
    /// <summary>Все пары, по дню недели и времени начала.</summary>
    Task<IReadOnlyList<ScheduleEntry>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ScheduleEntry>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<ScheduleEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(ScheduleEntry entry, CancellationToken ct = default);

    Task UpdateAsync(ScheduleEntry entry, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class ScheduleRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IScheduleRepository
{
    public async Task<IReadOnlyList<ScheduleEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ScheduleEntries
            .AsNoTracking()
            .OrderBy(x => x.DayOfWeek)
            .ThenBy(x => x.StartTime)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScheduleEntry>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ScheduleEntries
            .AsNoTracking()
            .Where(x => x.SubjectId == subjectId)
            .OrderBy(x => x.DayOfWeek)
            .ThenBy(x => x.StartTime)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ScheduleEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ScheduleEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ScheduleEntry entry, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ScheduleEntries.Add(entry);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ScheduleEntry entry, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ScheduleEntries.Update(entry);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.ScheduleEntries
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
