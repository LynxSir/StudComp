using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к истории задач генерации отчётов (ARCHITECTURE §7.3, §10.3).</summary>
public interface IReportJobRepository
{
    /// <summary>Последние <paramref name="count"/> задач, свежие сверху — для списка «Отчёты».</summary>
    Task<IReadOnlyList<ReportJob>> GetRecentAsync(int count, CancellationToken ct = default);

    Task<ReportJob?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Сколько задач ссылается на шаблон — проверка перед удалением профиля оформления.</summary>
    Task<int> CountByTemplateAsync(Guid templateId, CancellationToken ct = default);

    Task AddAsync(ReportJob job, CancellationToken ct = default);

    Task UpdateAsync(ReportJob job, CancellationToken ct = default);

    /// <summary>Удаляет запись истории. Файл <c>.docx</c> на диске не трогает — это метаданные, не файл пользователя.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class ReportJobRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IReportJobRepository
{
    public async Task<IReadOnlyList<ReportJob>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ReportJobs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(count)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ReportJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ReportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<int> CountByTemplateAsync(Guid templateId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ReportJobs
            .AsNoTracking()
            .CountAsync(x => x.TemplateId == templateId, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ReportJob job, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ReportJobs.Add(job);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ReportJob job, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ReportJobs.Update(job);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.ReportJobs
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
