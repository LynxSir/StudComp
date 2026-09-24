using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к оценкам зачётки (ARCHITECTURE §7.3, §9.4).</summary>
public interface IGradeRepository
{
    /// <summary>Все оценки по предмету, по возрастанию даты — вход для <c>IGradeForecastStrategy</c>.</summary>
    Task<IReadOnlyList<GradeEntry>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>Одна запись по Id или <see langword="null"/> — для проверки «существует ли» в сервисе.</summary>
    Task<GradeEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(GradeEntry grade, CancellationToken ct = default);

    Task UpdateAsync(GradeEntry grade, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class GradeRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IGradeRepository
{
    public async Task<IReadOnlyList<GradeEntry>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.GradeEntries
            .AsNoTracking()
            .Where(x => x.SubjectId == subjectId)
            .OrderBy(x => x.Date)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<GradeEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.GradeEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(GradeEntry grade, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.GradeEntries.Add(grade);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(GradeEntry grade, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.GradeEntries.Update(grade);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.GradeEntries
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
