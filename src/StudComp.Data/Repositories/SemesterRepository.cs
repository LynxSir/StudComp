using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к учебным семестрам (ARCHITECTURE §7.3, new_addons.md §5).</summary>
public interface ISemesterRepository
{
    Task<IReadOnlyList<Semester>> GetAllAsync(CancellationToken ct = default);

    Task<Semester?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Активный семестр либо <see langword="null"/>, если ни один не отмечен.</summary>
    Task<Semester?> GetActiveAsync(CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    Task AddAsync(Semester semester, CancellationToken ct = default);

    Task UpdateAsync(Semester semester, CancellationToken ct = default);

    /// <summary>Удаляет семестр; <c>Subject.SemesterId</c> у его предметов обнуляется (ARCHITECTURE §7.2).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Делает семестр активным, снимая флаг со всех остальных. Обе записи меняются одним
    /// <c>SaveChanges</c> — «двух активных семестров» не бывает даже на мгновение.
    /// </summary>
    Task SetActiveAsync(Guid id, CancellationToken ct = default);
}

internal sealed class SemesterRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), ISemesterRepository
{
    public async Task<IReadOnlyList<Semester>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Semesters
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Semester?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Semesters
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Semester?> GetActiveAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Semesters
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, ct)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Semesters.CountAsync(ct).ConfigureAwait(false);
    }

    public async Task AddAsync(Semester semester, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Semesters.Add(semester);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Semester semester, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Semesters.Update(semester);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.Semesters
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task SetActiveAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var all = await context.Semesters.ToListAsync(ct).ConfigureAwait(false);
        foreach (var semester in all)
        {
            semester.IsActive = semester.Id == id;
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
