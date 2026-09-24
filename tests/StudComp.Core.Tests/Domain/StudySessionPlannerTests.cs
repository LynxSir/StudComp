using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты планировщика порядка сессии (new_addons.md §5.4, Phase 12.8). Порядок обязан быть
/// чистой функцией от зерна: на этом стоит и «пройти ту же сессию ещё раз», и сама тестируемость.
/// </summary>
public sealed class StudySessionPlannerTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Guid Id(int index) => Guid.Parse($"00000000-0000-0000-0000-{index:D12}");

    private static StudyCandidate Card(
        int index,
        int deck = 0,
        int lapses = 0,
        double ease = 2.5,
        int lastReviewedDaysAgo = -1,
        int? dueInDays = null,
        int createdDaysAgo = 0,
        int deckSortOrder = 0) =>
        new(
            Id(index),
            deck == 0 ? null : Id(1000 + deck),
            null,
            deckSortOrder,
            lapses,
            ease,
            lastReviewedDaysAgo < 0 ? null : Epoch.AddDays(-lastReviewedDaysAgo),
            dueInDays is null ? null : Epoch.AddDays(dueInDays.Value),
            Epoch.AddDays(-createdDaysAgo));

    private static List<StudyCandidate> Pool(int count, int decks = 0) =>
        [.. Enumerable.Range(0, count).Select(i => Card(i, decks == 0 ? 0 : (i % decks) + 1))];

    // --- воспроизводимость -----------------------------------------------------------------------

    [Fact]
    public void The_same_seed_gives_the_same_order()
    {
        var pool = Pool(40, decks: 3);
        var first = StudySessionPlanner.Build(pool, new StudyPlanOptions(), 4242);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(first, StudySessionPlanner.Build(pool, new StudyPlanOptions(), 4242));
        }
    }

    [Fact]
    public void Different_seeds_give_different_orders()
    {
        var pool = Pool(30);
        var distinct = new HashSet<string>();

        for (var seed = 1; seed <= 50; seed++)
        {
            var order = StudySessionPlanner.Build(pool, new StudyPlanOptions(SpreadDecks: false), seed);
            distinct.Add(string.Join(",", order));
        }

        Assert.True(distinct.Count >= 45, $"из 50 зёрен получилось всего {distinct.Count} различных порядков");
    }

    [Fact]
    public void The_order_for_a_pinned_seed_never_changes()
    {
        // Страж собственного PRNG: зерно лежит в базе годами, и «та же сессия» обязана остаться той же
        // после любого рефакторинга. Если этот тест упал — менять надо не его, а обратно алгоритм.
        var order = StudySessionPlanner.Build(
            Pool(10), new StudyPlanOptions(SpreadDecks: false), 12345);

        var indexes = string.Join(",", order.Select(id => id.ToString()[^1..]));

        Assert.Equal("1,8,2,4,7,6,5,9,3,0", indexes);
    }

    // --- порядки ---------------------------------------------------------------------------------

    [Fact]
    public void Deck_order_follows_deck_sort_order_then_creation()
    {
        List<StudyCandidate> pool =
        [
            Card(1, deck: 2, deckSortOrder: 20, createdDaysAgo: 1),
            Card(2, deck: 1, deckSortOrder: 10, createdDaysAgo: 1),
            Card(3, deck: 1, deckSortOrder: 10, createdDaysAgo: 5),
        ];

        var order = StudySessionPlanner.Build(pool, new StudyPlanOptions(StudyOrder.DeckOrder), 1);

        Assert.Equal([Id(3), Id(2), Id(1)], order);
    }

    [Fact]
    public void Hardest_first_puts_lapses_before_ease()
    {
        List<StudyCandidate> pool =
        [
            Card(1, lapses: 0, ease: 1.5),
            Card(2, lapses: 5, ease: 2.5),
            Card(3, lapses: 5, ease: 1.4),
        ];

        var order = StudySessionPlanner.Build(
            pool, new StudyPlanOptions(StudyOrder.HardestFirst, SpreadDecks: false), 1);

        Assert.Equal([Id(3), Id(2), Id(1)], order);
    }

    [Fact]
    public void Least_recently_seen_puts_never_seen_first()
    {
        List<StudyCandidate> pool =
        [
            Card(1, lastReviewedDaysAgo: 1),
            Card(2, lastReviewedDaysAgo: -1),
            Card(3, lastReviewedDaysAgo: 30),
        ];

        var order = StudySessionPlanner.Build(
            pool, new StudyPlanOptions(StudyOrder.LeastRecentlySeen, SpreadDecks: false), 1);

        Assert.Equal([Id(2), Id(3), Id(1)], order);
    }

    [Fact]
    public void Newest_first_sorts_by_creation_descending()
    {
        List<StudyCandidate> pool =
        [
            Card(1, createdDaysAgo: 10),
            Card(2, createdDaysAgo: 1),
            Card(3, createdDaysAgo: 5),
        ];

        var order = StudySessionPlanner.Build(
            pool, new StudyPlanOptions(StudyOrder.NewestFirst, SpreadDecks: false), 1);

        Assert.Equal([Id(2), Id(3), Id(1)], order);
    }

    [Fact]
    public void Due_first_puts_the_most_overdue_ahead_and_new_cards_last()
    {
        List<StudyCandidate> pool =
        [
            Card(1, dueInDays: -1),
            Card(2, dueInDays: null),      // новая — срока нет
            Card(3, dueInDays: -10),
        ];

        var order = StudySessionPlanner.Build(
            pool, new StudyPlanOptions(StudyOrder.DueFirst, SpreadDecks: false), 1);

        Assert.Equal([Id(3), Id(1), Id(2)], order);
    }

    [Fact]
    public void Ties_are_broken_by_identifier_so_the_database_order_never_leaks_through()
    {
        var pool = Pool(5);
        var reversed = Enumerable.Reverse(pool).ToList();

        var direct = StudySessionPlanner.Build(
            pool, new StudyPlanOptions(StudyOrder.HardestFirst, SpreadDecks: false), 1);
        var shuffled = StudySessionPlanner.Build(
            reversed, new StudyPlanOptions(StudyOrder.HardestFirst, SpreadDecks: false), 1);

        Assert.Equal(direct, shuffled);
    }

    // --- разведение колод ------------------------------------------------------------------------

    [Fact]
    public void Cards_of_one_deck_are_not_placed_side_by_side()
    {
        var pool = Pool(30, decks: 3);
        var byId = pool.ToDictionary(c => c.CardId, c => c.DeckId);

        var order = StudySessionPlanner.Build(pool, new StudyPlanOptions(), 7);

        for (var i = 1; i < order.Count; i++)
        {
            Assert.NotEqual(byId[order[i - 1]], byId[order[i]]);
        }
    }

    [Fact]
    public void A_single_deck_does_not_hang_the_planner()
    {
        // Разнообразие из ничего не выдумывается: соседство честно остаётся, но проход завершается.
        var pool = Pool(20, decks: 1);

        var order = StudySessionPlanner.Build(pool, new StudyPlanOptions(), 3);

        Assert.Equal(20, order.Count);
        Assert.Equal(20, order.Distinct().Count());
    }

    [Fact]
    public void Deck_order_is_not_disturbed_by_spreading()
    {
        var pool = new List<StudyCandidate>
        {
            Card(1, deck: 1, deckSortOrder: 10),
            Card(2, deck: 1, deckSortOrder: 10, createdDaysAgo: -1),
            Card(3, deck: 2, deckSortOrder: 20),
        };

        var order = StudySessionPlanner.Build(
            pool, new StudyPlanOptions(StudyOrder.DeckOrder, SpreadDecks: true), 1);

        Assert.Equal([Id(1), Id(2), Id(3)], order);
    }

    // --- лимит, дубликаты, пустота ----------------------------------------------------------------

    [Fact]
    public void Max_count_truncates_the_plan()
    {
        var order = StudySessionPlanner.Build(Pool(100), new StudyPlanOptions(MaxCount: 40), 5);

        Assert.Equal(40, order.Count);
    }

    [Fact]
    public void Max_count_larger_than_the_pool_is_harmless()
    {
        var order = StudySessionPlanner.Build(Pool(7), new StudyPlanOptions(MaxCount: 40), 5);

        Assert.Equal(7, order.Count);
    }

    [Fact]
    public void Duplicates_are_dropped_keeping_the_first_occurrence()
    {
        List<StudyCandidate> pool = [Card(1), Card(2), Card(1), Card(2)];

        var order = StudySessionPlanner.Build(
            pool, new StudyPlanOptions(StudyOrder.DeckOrder), 1);

        Assert.Equal([Id(1), Id(2)], order);
    }

    [Fact]
    public void An_empty_pool_gives_an_empty_plan()
    {
        Assert.Empty(StudySessionPlanner.Build([], new StudyPlanOptions(), 1));
    }

    // --- доля новых -------------------------------------------------------------------------------

    [Fact]
    public void New_cards_are_mixed_in_by_share_rather_than_dumped_at_the_front()
    {
        List<StudyCandidate> pool =
        [
            .. Enumerable.Range(0, 16).Select(i => Card(i, dueInDays: -1)),
            .. Enumerable.Range(100, 4).Select(i => Card(i)),
        ];

        var order = StudySessionPlanner.Build(
            pool,
            new StudyPlanOptions(StudyOrder.DueFirst, MaxCount: 20, SpreadDecks: false, NewCardShare: 0.2),
            11);

        var fresh = pool.Where(c => c.IsNew).Select(c => c.CardId).ToHashSet();
        var newPositions = order
            .Select((id, index) => (id, index))
            .Where(x => fresh.Contains(x.id))
            .Select(x => x.index)
            .ToList();

        Assert.Equal(20, order.Count);
        Assert.Equal(4, newPositions.Count);
        Assert.True(newPositions.Max() - newPositions.Min() >= 10, "новые сгрудились в одном месте");
    }

    [Fact]
    public void A_short_supply_of_new_cards_is_topped_up_with_reviews()
    {
        List<StudyCandidate> pool =
        [
            .. Enumerable.Range(0, 30).Select(i => Card(i, dueInDays: -1)),
            Card(100),
        ];

        var order = StudySessionPlanner.Build(
            pool,
            new StudyPlanOptions(StudyOrder.DueFirst, MaxCount: 20, SpreadDecks: false, NewCardShare: 0.5),
            1);

        Assert.Equal(20, order.Count);
    }

    [Fact]
    public void The_share_still_produces_a_reproducible_order()
    {
        List<StudyCandidate> pool =
        [
            .. Enumerable.Range(0, 20).Select(i => Card(i, dueInDays: -1)),
            .. Enumerable.Range(100, 10).Select(i => Card(i)),
        ];

        var options = new StudyPlanOptions(MaxCount: 25, NewCardShare: 0.3);

        Assert.Equal(
            StudySessionPlanner.Build(pool, options, 99),
            StudySessionPlanner.Build(pool, options, 99));
    }
}
