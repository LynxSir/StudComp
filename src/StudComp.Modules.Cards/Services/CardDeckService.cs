using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Колоды карточек (new_addons.md §3.1). Один тип с двумя поведениями: обычная колода хранит
/// карточки по ссылке, «умная подборка» — сохранённый поисковый запрос, который вычисляется на лету.
/// </summary>
public interface ICardDeckService
{
    Task<IReadOnlyList<CardDeck>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CardDeck>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<CardDeck?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Содержимое колоды. У обычной — её карточки, у умной подборки — результат сохранённого
    /// запроса, поэтому она всегда свежая и никогда не расходится с картотекой.
    /// </summary>
    Task<IReadOnlyList<Card>> GetCardsAsync(Guid deckId, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(CardDeck deck, CancellationToken ct = default);

    Task<Result> UpdateAsync(CardDeck deck, CancellationToken ct = default);

    /// <summary>Удалить колоду. Карточки остаются — у них просто пропадает привязка.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class CardDeckService(
    ICardDeckRepository decks,
    ICardRepository cards,
    ICardSearchRepository search,
    ISubjectRepository subjects) : ICardDeckService
{
    private const int MaxNameLength = 200;

    /// <summary>Потолок выдачи умной подборки — столько же карточек в тренировке всё равно не нужно.</summary>
    private const int SmartDeckLimit = 500;

    public Task<IReadOnlyList<CardDeck>> GetAllAsync(CancellationToken ct = default) =>
        decks.GetAllAsync(ct);

    public Task<IReadOnlyList<CardDeck>> GetBySubjectAsync(
        Guid subjectId,
        CancellationToken ct = default) =>
        decks.GetBySubjectAsync(subjectId, ct);

    public Task<CardDeck?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        decks.GetByIdAsync(id, ct);

    public async Task<IReadOnlyList<Card>> GetCardsAsync(Guid deckId, CancellationToken ct = default)
    {
        var deck = await decks.GetByIdAsync(deckId, ct).ConfigureAwait(false);
        if (deck is null)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(deck.QueryExpression))
        {
            return await cards.GetByDeckAsync(deckId, ct).ConfigureAwait(false);
        }

        var spec = CardQuery.Parse(deck.QueryExpression);
        var hits = await search
            .SearchAsync(spec, new CardSearchOptions(Limit: SmartDeckLimit), ct)
            .ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return [];
        }

        var ids = hits.Select(x => x.CardId).ToList();
        var found = await cards.GetByIdsAsync(ids, ct).ConfigureAwait(false);
        var byId = found.ToDictionary(x => x.Id);

        return ids.Select(id => byId.GetValueOrDefault(id)).OfType<Card>().ToList();
    }

    public async Task<Result<Guid>> CreateAsync(CardDeck deck, CancellationToken ct = default)
    {
        Guard.NotNull(deck);

        var validation = await ValidateAsync(deck, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return Result<Guid>.Failure(validation.Error);
        }

        deck.Id = deck.Id == Guid.Empty ? Guid.NewGuid() : deck.Id;
        Normalize(deck);

        var now = DateTimeOffset.Now;
        deck.CreatedAt = deck.CreatedAt == default ? now : deck.CreatedAt;
        deck.UpdatedAt = now;

        await decks.AddAsync(deck, ct).ConfigureAwait(false);
        return Result<Guid>.Success(deck.Id);
    }

    public async Task<Result> UpdateAsync(CardDeck deck, CancellationToken ct = default)
    {
        Guard.NotNull(deck);

        var existing = await decks.GetByIdAsync(deck.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Result.Failure("cards.deck_not_found", "Колода не найдена.");
        }

        var validation = await ValidateAsync(deck, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return validation;
        }

        Normalize(deck);
        deck.CreatedAt = existing.CreatedAt;
        deck.UpdatedAt = DateTimeOffset.Now;

        await decks.UpdateAsync(deck, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await decks.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("cards.deck_not_found", "Колода не найдена.");
        }

        await decks.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> ValidateAsync(CardDeck deck, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deck.Name))
        {
            return Result.Failure("cards.deck_name_required", "У колоды должно быть название.");
        }

        if (deck.SubjectId is { } subjectId
            && await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("cards.subject_not_found", "Предмет не найден.");
        }

        return Result.Success();
    }

    private static void Normalize(CardDeck deck)
    {
        var name = deck.Name.Trim();
        deck.Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name;

        // Пустой запрос и отсутствие запроса — одно и то же: обычная колода.
        deck.QueryExpression = string.IsNullOrWhiteSpace(deck.QueryExpression)
            ? null
            : deck.QueryExpression.Trim();
    }
}
