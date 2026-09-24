using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к шаблонам оформления отчётов (ARCHITECTURE §7.3, §10.4).</summary>
public interface IReportTemplateRepository
{
    Task<IReadOnlyList<ReportTemplate>> GetAllAsync(CancellationToken ct = default);

    Task<ReportTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Шаблон по обозначению варианта ГОСТ («7.32-2017») — на нём держится идемпотентный сид.</summary>
    Task<ReportTemplate?> GetByGostVariantAsync(string gostVariant, CancellationToken ct = default);

    Task AddAsync(ReportTemplate template, CancellationToken ct = default);

    Task UpdateAsync(ReportTemplate template, CancellationToken ct = default);

    /// <summary>Удалить шаблон. Вызывающий сам следит, что на него не ссылается история отчётов.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class ReportTemplateRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IReportTemplateRepository
{
    public async Task<IReadOnlyList<ReportTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ReportTemplates
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ReportTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ReportTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<ReportTemplate?> GetByGostVariantAsync(string gostVariant, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ReportTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.GostVariant == gostVariant, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ReportTemplate template, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ReportTemplates.Add(template);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ReportTemplate template, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ReportTemplates.Update(template);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.ReportTemplates
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
