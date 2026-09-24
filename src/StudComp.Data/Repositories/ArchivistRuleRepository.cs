using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к правилам сортировки архивариуса (ARCHITECTURE §7.3, §8.4, §8.7).</summary>
public interface IArchivistRuleRepository
{
    /// <summary>
    /// Включённые правила по убыванию приоритета — в порядке, в котором их проверяет
    /// <c>ISortingRuleEngine</c> (ARCHITECTURE §8.4 п.3).
    /// </summary>
    Task<IReadOnlyList<ArchivistRule>> GetEnabledOrderedByPriorityAsync(CancellationToken ct = default);

    /// <summary>Правила конкретного предмета либо общие (<paramref name="subjectId"/> = <see langword="null"/>).</summary>
    Task<IReadOnlyList<ArchivistRule>> GetBySubjectAsync(Guid? subjectId, CancellationToken ct = default);

    /// <summary>Все правила, включая выключенные, по убыванию приоритета - список редактора правил.</summary>
    Task<IReadOnlyList<ArchivistRule>> GetAllAsync(CancellationToken ct = default);

    Task<ArchivistRule?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(ArchivistRule rule, CancellationToken ct = default);

    Task UpdateAsync(ArchivistRule rule, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Добавить пачку правил за одну транзакцию (импорт «добавить к существующим»).</summary>
    Task AddManyAsync(IEnumerable<ArchivistRule> rules, CancellationToken ct = default);

    /// <summary>Обновить пачку правил за одну транзакцию (перестановка приоритетов drag-n-drop).</summary>
    Task UpdateManyAsync(IEnumerable<ArchivistRule> rules, CancellationToken ct = default);

    /// <summary>Заменить весь набор правил целиком (импорт «заменить все»).</summary>
    Task ReplaceAllAsync(IEnumerable<ArchivistRule> rules, CancellationToken ct = default);
}

internal sealed class ArchivistRuleRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IArchivistRuleRepository
{
    public async Task<IReadOnlyList<ArchivistRule>> GetEnabledOrderedByPriorityAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ArchivistRules
            .AsNoTracking()
            .Where(x => x.Enabled)
            .OrderByDescending(x => x.Priority)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ArchivistRule>> GetBySubjectAsync(Guid? subjectId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ArchivistRules
            .AsNoTracking()
            .Where(x => x.SubjectId == subjectId)
            .OrderByDescending(x => x.Priority)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ArchivistRule>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ArchivistRules
            .AsNoTracking()
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Pattern)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ArchivistRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.ArchivistRules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ArchivistRule rule, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ArchivistRules.Add(rule);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ArchivistRule rule, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ArchivistRules.Update(rule);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.ArchivistRules
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddManyAsync(IEnumerable<ArchivistRule> rules, CancellationToken ct = default)
    {
        var list = rules as IReadOnlyList<ArchivistRule> ?? rules.ToList();
        if (list.Count == 0)
        {
            return;
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ArchivistRules.AddRange(list);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateManyAsync(IEnumerable<ArchivistRule> rules, CancellationToken ct = default)
    {
        var list = rules as IReadOnlyList<ArchivistRule> ?? rules.ToList();
        if (list.Count == 0)
        {
            return;
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.ArchivistRules.UpdateRange(list);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ReplaceAllAsync(IEnumerable<ArchivistRule> rules, CancellationToken ct = default)
    {
        var list = rules as IReadOnlyList<ArchivistRule> ?? rules.ToList();

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.ArchivistRules.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        if (list.Count > 0)
        {
            context.ArchivistRules.AddRange(list);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
