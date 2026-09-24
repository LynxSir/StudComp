using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>
/// Доступ к меткам и связкам «карточка ↔ метка» (new_addons.md §3.1). Связки живут здесь же:
/// отдельный репозиторий на join-таблицу из двух колонок был бы слоем ради слоя.
/// </summary>
public interface ICardTagRepository
{
    Task<IReadOnlyList<CardTag>> GetAllAsync(CancellationToken ct = default);

    Task<CardTag?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Метка по нормализованному имени — на нём стоит уникальный индекс.</summary>
    Task<CardTag?> GetByNameAsync(string name, CancellationToken ct = default);

    Task<IReadOnlyList<CardTag>> GetByNamesAsync(
        IReadOnlyList<string> names,
        CancellationToken ct = default);

    /// <summary>Самые частые метки — облако меток и автодополнение.</summary>
    Task<IReadOnlyList<CardTag>> GetTopAsync(int take, CancellationToken ct = default);

    Task AddAsync(CardTag tag, CancellationToken ct = default);

    Task UpdateAsync(CardTag tag, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Метки карточек пачкой — чтобы список библиотеки не делал запрос на строку.</summary>
    Task<IReadOnlyList<CardTagLink>> GetLinksAsync(
        IReadOnlyList<Guid> cardIds,
        CancellationToken ct = default);

    /// <summary>Полностью заменить набор меток карточки.</summary>
    Task SetLinksAsync(Guid cardId, IReadOnlyList<Guid> tagIds, CancellationToken ct = default);

    /// <summary>Повесить метку на карточки; уже висящие пропускаются.</summary>
    Task AddLinksAsync(IReadOnlyList<Guid> cardIds, Guid tagId, CancellationToken ct = default);

    /// <summary>Снять метку с карточек.</summary>
    Task RemoveLinksAsync(IReadOnlyList<Guid> cardIds, Guid tagId, CancellationToken ct = default);

    /// <summary>Перенести все связки одной метки на другую — слияние меток.</summary>
    Task MoveLinksAsync(Guid fromTagId, Guid toTagId, CancellationToken ct = default);

    /// <summary>
    /// Пересчитать денормализованные счётчики использований по фактическим связкам.
    /// Возвращает, сколько меток изменилось.
    /// </summary>
    Task<int> RecalculateUsageAsync(CancellationToken ct = default);
}

internal sealed class CardTagRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), ICardTagRepository
{
    public async Task<IReadOnlyList<CardTag>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardTags
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<CardTag?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardTags
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<CardTag?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardTags
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == name, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CardTag>> GetByNamesAsync(
        IReadOnlyList<string> names,
        CancellationToken ct = default)
    {
        if (names.Count == 0)
        {
            return [];
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardTags
            .AsNoTracking()
            .Where(x => names.Contains(x.Name))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CardTag>> GetTopAsync(int take, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardTags
            .AsNoTracking()
            .OrderByDescending(x => x.UsageCount)
            .ThenBy(x => x.Name)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(CardTag tag, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.CardTags.Add(tag);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(CardTag tag, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.CardTags.Update(tag);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.CardTags
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CardTagLink>> GetLinksAsync(
        IReadOnlyList<Guid> cardIds,
        CancellationToken ct = default)
    {
        if (cardIds.Count == 0)
        {
            return [];
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardTagLinks
            .AsNoTracking()
            .Where(x => cardIds.Contains(x.CardId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task SetLinksAsync(
        Guid cardId,
        IReadOnlyList<Guid> tagIds,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var existing = await context.CardTagLinks
            .Where(x => x.CardId == cardId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Снимаем только лишнее и вешаем только недостающее: каждая правка связки дёргает триггер
        // поискового индекса, и «снести всё и залить заново» стоило бы вдвое дороже без нужды.
        var wanted = tagIds.ToHashSet();

        var removed = existing.Where(x => !wanted.Contains(x.TagId)).ToList();
        context.CardTagLinks.RemoveRange(removed);

        var present = existing.Select(x => x.TagId).ToHashSet();
        var added = wanted
            .Where(tagId => !present.Contains(tagId))
            .Select(tagId => new CardTagLink { CardId = cardId, TagId = tagId })
            .ToList();
        context.CardTagLinks.AddRange(added);

        if (removed.Count > 0 || added.Count > 0)
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task AddLinksAsync(
        IReadOnlyList<Guid> cardIds,
        Guid tagId,
        CancellationToken ct = default)
    {
        if (cardIds.Count == 0)
        {
            return;
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var present = await context.CardTagLinks
            .Where(x => x.TagId == tagId && cardIds.Contains(x.CardId))
            .Select(x => x.CardId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var missing = cardIds
            .Distinct()
            .Where(cardId => !present.Contains(cardId))
            .Select(cardId => new CardTagLink { CardId = cardId, TagId = tagId })
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        context.CardTagLinks.AddRange(missing);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveLinksAsync(
        IReadOnlyList<Guid> cardIds,
        Guid tagId,
        CancellationToken ct = default)
    {
        if (cardIds.Count == 0)
        {
            return;
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.CardTagLinks
            .Where(x => x.TagId == tagId && cardIds.Contains(x.CardId))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task MoveLinksAsync(Guid fromTagId, Guid toTagId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var source = await context.CardTagLinks
            .Where(x => x.TagId == fromTagId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (source.Count == 0)
        {
            return;
        }

        var alreadyTagged = await context.CardTagLinks
            .Where(x => x.TagId == toTagId)
            .Select(x => x.CardId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Составной первичный ключ не даст завести вторую такую же пару, поэтому карточки, у
        // которых целевая метка уже есть, просто теряют исходную связку.
        context.CardTagLinks.RemoveRange(source);
        context.CardTagLinks.AddRange(source
            .Where(x => !alreadyTagged.Contains(x.CardId))
            .Select(x => new CardTagLink { CardId = x.CardId, TagId = toTagId }));

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> RecalculateUsageAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var counts = await context.CardTagLinks
            .AsNoTracking()
            .GroupBy(x => x.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var actual = counts.ToDictionary(x => x.TagId, x => x.Count);
        var tags = await context.CardTags.ToListAsync(ct).ConfigureAwait(false);

        var changed = 0;
        foreach (var tag in tags)
        {
            var count = actual.GetValueOrDefault(tag.Id);
            if (tag.UsageCount == count)
            {
                continue;
            }

            tag.UsageCount = count;
            changed++;
        }

        if (changed > 0)
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changed;
    }
}
