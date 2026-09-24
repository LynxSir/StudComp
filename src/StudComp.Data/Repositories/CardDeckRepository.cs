using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к колодам карточек (new_addons.md §3.1).</summary>
public interface ICardDeckRepository
{
    Task<IReadOnlyList<CardDeck>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Колоды предмета в заданном пользователем порядке.</summary>
    Task<IReadOnlyList<CardDeck>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<CardDeck?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Колода по имени — так её резолвит оператор <c>колода:…</c> в строке поиска и импорт из файла,
    /// где чужой <see cref="Guid"/> бессмыслен.
    /// </summary>
    Task<CardDeck?> GetByNameAsync(string name, CancellationToken ct = default);

    Task AddAsync(CardDeck deck, CancellationToken ct = default);

    Task UpdateAsync(CardDeck deck, CancellationToken ct = default);

    /// <summary>
    /// Удалить колоду. Карточки при этом остаются: ссылка на колоду обнуляется каскадом
    /// <c>SET NULL</c> (new_addons.md §3.3).
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class CardDeckRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), ICardDeckRepository
{
    public async Task<IReadOnlyList<CardDeck>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardDecks
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CardDeck>> GetBySubjectAsync(
        Guid subjectId,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardDecks
            .AsNoTracking()
            .Where(x => x.SubjectId == subjectId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<CardDeck?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardDecks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<CardDeck?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardDecks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == name, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(CardDeck deck, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.CardDecks.Add(deck);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(CardDeck deck, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.CardDecks.Update(deck);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.CardDecks
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
