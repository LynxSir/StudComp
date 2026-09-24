using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к предметам (ARCHITECTURE §7.3).</summary>
public interface ISubjectRepository
{
    Task<IReadOnlyList<Subject>> GetAllAsync(CancellationToken ct = default);

    Task<Subject?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Subject subject, CancellationToken ct = default);

    Task UpdateAsync(Subject subject, CancellationToken ct = default);

    /// <summary>Удаляет предмет; связанные расписание, дедлайны и оценки уходят каскадом (ARCHITECTURE §7.2).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class SubjectRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), ISubjectRepository
{
    public async Task<IReadOnlyList<Subject>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Subjects
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Subject?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(Subject subject, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Subjects.Add(subject);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Subject subject, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Subjects.Update(subject);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.Subjects
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
