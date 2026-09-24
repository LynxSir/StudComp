using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Порядок выдачи библиотеки (new_addons.md §8.2) и постраничность смещением. Как и остальные
/// тесты поиска, каждый факт прогоняется <b>через обе</b> реализации: быструю на FTS5 и фолбэк —
/// расхождение между ними означало бы, что сортировка врёт в одном из режимов.
/// </summary>
public sealed class CardSearchSortTests : DatabaseTestBase
{
    /// <summary><c>true</c> — путь через FTS5, <c>false</c> — фолбэк без индекса.</summary>
    public static TheoryData<bool> Engines => new() { true, false };

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Recently_updated_comes_first(bool fullText)
    {
        var old = Card("Интеграл старый", updatedDaysAgo: 10);
        var fresh = Card("Интеграл свежий", updatedDaysAgo: 1);
        var middle = Card("Интеграл средний", updatedDaysAgo: 5);
        await SeedAsync(old, fresh, middle);

        var found = await FindAsync(fullText, "интеграл", CardSortOrder.RecentlyUpdated);

        Assert.Equal([fresh.Id, middle.Id, old.Id], found);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Recently_created_comes_first(bool fullText)
    {
        var old = Card("Интеграл старый", createdDaysAgo: 30);
        var fresh = Card("Интеграл свежий", createdDaysAgo: 2);
        await SeedAsync(old, fresh);

        var found = await FindAsync(fullText, "интеграл", CardSortOrder.RecentlyCreated);

        Assert.Equal([fresh.Id, old.Id], found);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Alphabetical_order_is_by_the_front_side(bool fullText)
    {
        var gamma = Card("Гамма-функция");
        var alpha = Card("Альфа-канал");
        var beta = Card("Бета-распределение");
        await SeedAsync(gamma, alpha, beta);

        var found = await FindAsync(fullText, string.Empty, CardSortOrder.Alphabetical);

        Assert.Equal([alpha.Id, beta.Id, gamma.Id], found);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Due_order_puts_the_nearest_first_and_new_cards_last(bool fullText)
    {
        var soon = Card("Интеграл скоро", dueInDays: 1);
        var later = Card("Интеграл позже", dueInDays: 7);
        var never = Card("Интеграл новый");
        await SeedAsync(never, later, soon);

        var found = await FindAsync(fullText, "интеграл", CardSortOrder.DueDate);

        Assert.Equal([soon.Id, later.Id, never.Id], found);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Pinned_cards_stay_on_top_of_every_explicit_order(bool fullText)
    {
        // Закреплённая карточка алфавитно последняя — и всё равно обязана быть первой.
        var pinned = Card("Явление Гиббса", pinned: true);
        var alpha = Card("Альфа-канал");
        await SeedAsync(pinned, alpha);

        var found = await FindAsync(fullText, string.Empty, CardSortOrder.Alphabetical);

        Assert.Equal([pinned.Id, alpha.Id], found);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Relevance_stays_the_default_order(bool fullText)
    {
        var pinned = Card("Интеграл закреплённый", pinned: true);
        var ordinary = Card("Интеграл обычный");
        await SeedAsync(ordinary, pinned);

        var found = await FindAsync(fullText, "интеграл", CardSortOrder.Relevance);

        Assert.Equal(pinned.Id, found[0]);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Paging_by_offset_covers_everything_without_repeats(bool fullText)
    {
        var cards = Enumerable
            .Range(1, 5)
            .Select(i => Card($"Интеграл {i:00}", updatedDaysAgo: i))
            .ToArray();

        await SeedAsync(cards);

        var first = await FindAsync(fullText, "интеграл", CardSortOrder.RecentlyUpdated, limit: 2);
        var second = await FindAsync(fullText, "интеграл", CardSortOrder.RecentlyUpdated, limit: 2, offset: 2);
        var third = await FindAsync(fullText, "интеграл", CardSortOrder.RecentlyUpdated, limit: 2, offset: 4);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Single(third);

        var all = first.Concat(second).Concat(third).ToList();
        Assert.Equal(5, all.Distinct().Count());
        Assert.Equal(cards.Select(x => x.Id).OrderBy(x => x), all.OrderBy(x => x));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Sorting_works_together_with_a_full_text_query(bool fullText)
    {
        var matching = Card("Гамма-функция интеграл");
        var alsoMatching = Card("Альфа-канал интеграл");

        // Оборот у этой карточки свой: в тексте по умолчанию слово «интеграл» есть, и она
        // попала бы в выдачу законно, но проверяем мы здесь не полноту, а порядок.
        var other = Card("Бета-распределение", back: "Плотность вероятности на отрезке.");
        await SeedAsync(matching, alsoMatching, other);

        var found = await FindAsync(fullText, "интеграл", CardSortOrder.Alphabetical);

        Assert.Equal([alsoMatching.Id, matching.Id], found);
    }

    private static Card Card(
        string front,
        int updatedDaysAgo = 0,
        int createdDaysAgo = 0,
        int? dueInDays = null,
        bool pinned = false,
        string? back = null)
    {
        var card = back is null ? TestData.Card(front: front) : TestData.Card(front: front, back: back);
        card.CreatedAt = DateTimeOffset.UtcNow.AddDays(-createdDaysAgo);
        card.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-updatedDaysAgo);
        card.DueAt = dueInDays is { } days ? DateTimeOffset.UtcNow.AddDays(days) : null;
        card.IsPinned = pinned;
        return card;
    }

    private ICardSearchRepository Engine(bool fullText) => fullText
        ? new CardSearchRepository(Factory)
        : new FallbackCardSearchRepository(Factory);

    private async Task SeedAsync(params Card[] cards)
    {
        await using var context = CreateContext();
        context.Cards.AddRange(cards);
        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<Guid>> FindAsync(
        bool fullText,
        string query,
        CardSortOrder sort,
        int limit = 50,
        int offset = 0)
    {
        var hits = await Engine(fullText).SearchAsync(
            CardQuery.Parse(query),
            new CardSearchOptions(Limit: limit, Offset: offset, Sort: sort));

        return hits.Select(x => x.CardId).ToList();
    }
}
