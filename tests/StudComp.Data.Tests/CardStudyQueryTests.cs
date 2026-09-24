using System.Diagnostics;
using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Выборка пула карточек для тренажёра и запись результата ответа (new_addons.md §5.5, §6.1,
/// Phase 12.8). Всё это считает база: лимит без серверной сортировки вернул бы не те карточки,
/// а очередь дня обязана быть именно той самой.
/// </summary>
public sealed class CardStudyQueryTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // --- проекция ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_candidate_carries_everything_the_planner_needs()
    {
        var subject = TestData.Subject();
        var deck = TestData.CardDeck(subject.Id);
        deck.SortOrder = 17;

        var card = TestData.Card(subject.Id, deck.Id);
        card.Lapses = 3;
        card.EaseFactor = 1.9;
        card.DueAt = Now.AddDays(-2);
        card.LastReviewedAt = Now.AddDays(-9);

        await SeedAsync(subjects: [subject], decks: [deck], cards: [card]);

        var candidate = Assert.Single(
            await new CardRepository(Factory).GetStudyCandidatesAsync(new StudyPoolFilter()));

        Assert.Equal(card.Id, candidate.CardId);
        Assert.Equal(deck.Id, candidate.DeckId);
        Assert.Equal(subject.Id, candidate.SubjectId);
        Assert.Equal(17, candidate.DeckSortOrder);
        Assert.Equal(3, candidate.Lapses);
        Assert.Equal(1.9, candidate.EaseFactor, 6);
        Assert.False(candidate.IsNew);
    }

    [Fact]
    public async Task A_card_without_a_deck_gets_a_neutral_sort_order()
    {
        await SeedAsync(cards: [TestData.Card()]);

        var candidate = Assert.Single(
            await new CardRepository(Factory).GetStudyCandidatesAsync(new StudyPoolFilter()));

        Assert.Null(candidate.DeckId);
        Assert.Equal(0, candidate.DeckSortOrder);
        Assert.True(candidate.IsNew);
    }

    // --- фильтры ------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleted_cards_never_reach_the_trainer()
    {
        var alive = TestData.Card();
        var trashed = TestData.Card();
        trashed.DeletedAt = Now;

        await SeedAsync(cards: [alive, trashed]);

        var pool = await new CardRepository(Factory).GetStudyCandidatesAsync(
            new StudyPoolFilter(IncludeSuspended: true));

        Assert.Equal([alive.Id], pool.Select(c => c.CardId));
    }

    [Fact]
    public async Task Suspended_cards_are_skipped_unless_explicitly_asked_for()
    {
        var normal = TestData.Card();
        var suspended = TestData.Card();
        suspended.IsSuspended = true;

        await SeedAsync(cards: [normal, suspended]);

        var repository = new CardRepository(Factory);

        Assert.Equal(
            [normal.Id],
            (await repository.GetStudyCandidatesAsync(new StudyPoolFilter())).Select(c => c.CardId));
        Assert.Equal(
            2,
            (await repository.GetStudyCandidatesAsync(new StudyPoolFilter(IncludeSuspended: true))).Count);
    }

    [Fact]
    public async Task The_pool_can_be_narrowed_to_subjects_decks_and_kinds()
    {
        var math = TestData.Subject("Матан");
        var physics = TestData.Subject("Физика");
        var deck = TestData.CardDeck(math.Id);

        var wanted = TestData.Card(math.Id, deck.Id);
        wanted.Kind = CardKind.Term;

        var wrongKind = TestData.Card(math.Id, deck.Id);
        wrongKind.Kind = CardKind.Code;

        var wrongSubject = TestData.Card(physics.Id);
        wrongSubject.Kind = CardKind.Term;

        await SeedAsync(subjects: [math, physics], decks: [deck], cards: [wanted, wrongKind, wrongSubject]);

        var pool = await new CardRepository(Factory).GetStudyCandidatesAsync(new StudyPoolFilter(
            SubjectIds: [math.Id],
            DeckIds: [deck.Id],
            Kinds: [CardKind.Term]));

        Assert.Equal([wanted.Id], pool.Select(c => c.CardId));
    }

    [Fact]
    public async Task The_pool_can_be_narrowed_to_tags()
    {
        var tagged = TestData.Card();
        var untagged = TestData.Card();
        var tag = TestData.CardTag();

        await SeedAsync(cards: [tagged, untagged], tags: [tag], links: [(tagged.Id, tag.Id)]);

        var pool = await new CardRepository(Factory).GetStudyCandidatesAsync(
            new StudyPoolFilter(TagIds: [tag.Id]));

        Assert.Equal([tagged.Id], pool.Select(c => c.CardId));
    }

    [Fact]
    public async Task An_explicit_card_list_wins_over_everything_else()
    {
        var first = TestData.Card();
        var second = TestData.Card();
        var third = TestData.Card();

        await SeedAsync(cards: [first, second, third]);

        var pool = await new CardRepository(Factory).GetStudyCandidatesAsync(
            new StudyPoolFilter(CardIds: [first.Id, third.Id]));

        Assert.Equal(
            new[] { first.Id, third.Id }.OrderBy(id => id),
            pool.Select(c => c.CardId).OrderBy(id => id));
    }

    [Fact]
    public async Task New_and_due_cards_can_be_asked_for_separately()
    {
        var fresh = TestData.Card();
        var due = TestData.Card();
        due.DueAt = Now.AddDays(-1);
        var later = TestData.Card();
        later.DueAt = Now.AddDays(5);

        await SeedAsync(cards: [fresh, due, later]);

        var repository = new CardRepository(Factory);

        var newOnes = await repository.GetStudyCandidatesAsync(new StudyPoolFilter(OnlyNew: true));
        var dueOnes = await repository.GetStudyCandidatesAsync(new StudyPoolFilter(DueBefore: Now));
        var notNew = await repository.GetStudyCandidatesAsync(new StudyPoolFilter(ExcludeNew: true));

        Assert.Equal([fresh.Id], newOnes.Select(c => c.CardId));
        Assert.Equal([due.Id], dueOnes.Select(c => c.CardId));
        Assert.Equal(2, notNew.Count);
    }

    // --- лимит и порядок -------------------------------------------------------------------------------

    [Fact]
    public async Task A_limit_is_applied_after_the_server_side_sort_not_before()
    {
        // Ради этого метод и живёт в базе: клиентская обрезка взяла бы произвольные три карточки,
        // а очередь дня обязана начинаться с самых просроченных.
        var cards = Enumerable.Range(0, 20).Select(i =>
        {
            var card = TestData.Card(front: $"карточка {i}");
            card.DueAt = Now.AddDays(-i);
            return card;
        }).ToList();

        await SeedAsync(cards: [.. cards]);

        var pool = await new CardRepository(Factory).GetStudyCandidatesAsync(
            new StudyPoolFilter(DueBefore: Now, Sort: StudyPoolSort.DueAsc, Limit: 3));

        var expected = cards.OrderBy(c => c.DueAt).Take(3).Select(c => c.Id);

        Assert.Equal(expected, pool.Select(c => c.CardId));
    }

    [Fact]
    public async Task A_limit_without_a_sort_is_still_deterministic()
    {
        await SeedAsync(cards: [.. Enumerable.Range(0, 10).Select(_ => TestData.Card())]);

        var repository = new CardRepository(Factory);

        var first = await repository.GetStudyCandidatesAsync(new StudyPoolFilter(Limit: 4));
        var second = await repository.GetStudyCandidatesAsync(new StudyPoolFilter(Limit: 4));

        Assert.Equal(4, first.Count);
        Assert.Equal(first.Select(c => c.CardId), second.Select(c => c.CardId));
    }

    [Theory]
    [InlineData(StudyPoolSort.CreatedAsc)]
    [InlineData(StudyPoolSort.CreatedDesc)]
    [InlineData(StudyPoolSort.UpdatedDesc)]
    public async Task Every_sort_order_is_translated_to_sql(StudyPoolSort sort)
    {
        await SeedAsync(cards: [.. Enumerable.Range(0, 5).Select(_ => TestData.Card())]);

        var pool = await new CardRepository(Factory).GetStudyCandidatesAsync(new StudyPoolFilter(Sort: sort));

        Assert.Equal(5, pool.Count);
    }

    // --- счётчики -----------------------------------------------------------------------------------------

    [Fact]
    public async Task New_cards_are_counted_without_the_suspended_and_deleted_ones()
    {
        var fresh = TestData.Card();
        var suspended = TestData.Card();
        suspended.IsSuspended = true;
        var trashed = TestData.Card();
        trashed.DeletedAt = Now;
        var seen = TestData.Card();
        seen.DueAt = Now.AddDays(3);

        await SeedAsync(cards: [fresh, suspended, trashed, seen]);

        Assert.Equal(1, await new CardRepository(Factory).CountNewAvailableAsync());
    }

    // --- кривая нагрузки ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_forecast_groups_upcoming_cards_by_day()
    {
        var from = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        var cards = new List<Card>();
        foreach (var (day, count) in new[] { (0, 2), (1, 3), (4, 1) })
        {
            for (var i = 0; i < count; i++)
            {
                var card = TestData.Card();
                card.DueAt = from.AddDays(day).AddHours(10 + i);
                cards.Add(card);
            }
        }

        await SeedAsync(cards: [.. cards]);

        var forecast = await new CardRepository(Factory).GetDueForecastAsync(from, 30, 0);

        Assert.Equal(
            [
                new DueForecastBucket(new DateOnly(2026, 9, 10), 2),
                new DueForecastBucket(new DateOnly(2026, 9, 11), 3),
                new DueForecastBucket(new DateOnly(2026, 9, 14), 1),
            ],
            forecast);
    }

    [Fact]
    public async Task The_forecast_respects_the_study_day_shift()
    {
        var from = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        var card = TestData.Card();
        card.DueAt = new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);   // час ночи по UTC

        await SeedAsync(cards: [card]);

        var repository = new CardRepository(Factory);

        // Со сдвигом «минус четыре часа» ночная карточка относится к предыдущему учебному дню.
        Assert.Equal(new DateOnly(2026, 9, 11), (await repository.GetDueForecastAsync(from, 30, 0))[0].Day);
        Assert.Equal(new DateOnly(2026, 9, 10), (await repository.GetDueForecastAsync(from, 30, -240))[0].Day);
    }

    [Fact]
    public async Task The_forecast_is_empty_for_a_zero_horizon()
    {
        Assert.Empty(await new CardRepository(Factory).GetDueForecastAsync(Now, 0, 0));
    }

    // --- запись ответа ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Applying_a_review_touches_exactly_the_scheduling_columns()
    {
        var card = TestData.Card();
        card.UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(cards: [card]);

        var reviewedAt = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var outcome = new ReviewOutcome(6, 2.35, 2, 1, reviewedAt.AddDays(6), false);

        var changed = await new CardRepository(Factory).ApplyReviewAsync(card.Id, outcome, reviewedAt);

        await using var context = CreateContext();
        var saved = context.Cards.Single();

        Assert.Equal(1, changed);
        Assert.Equal(outcome.DueAt, saved.DueAt);
        Assert.Equal(6, saved.IntervalDays, 6);
        Assert.Equal(2.35, saved.EaseFactor, 6);
        Assert.Equal(2, saved.Repetitions);
        Assert.Equal(1, saved.Lapses);
        Assert.Equal(reviewedAt, saved.LastReviewedAt);

        // Повторение — не правка карточки: иначе поехала бы сортировка «недавно изменённые»
        // и лишний раз сработал бы триггер поискового индекса.
        Assert.Equal(card.UpdatedAt, saved.UpdatedAt);
        Assert.Equal(card.Front, saved.Front);
        Assert.Equal(card.Back, saved.Back);
    }

    [Fact]
    public async Task Applying_a_review_to_a_missing_card_changes_nothing()
    {
        var outcome = new ReviewOutcome(1, 2.5, 1, 0, Now.AddDays(1), false);

        Assert.Equal(0, await new CardRepository(Factory).ApplyReviewAsync(Guid.NewGuid(), outcome, Now));
    }

    // --- нагрузка -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_days_queue_over_five_thousand_cards_stays_within_the_budget()
    {
        var cards = Enumerable.Range(0, 5000).Select(i =>
        {
            var card = TestData.Card(front: $"карточка {i}");
            card.DueAt = Now.AddDays(-(i % 30));
            return card;
        }).ToArray();

        await SeedAsync(cards: cards);

        var repository = new CardRepository(Factory);
        await repository.GetStudyCandidatesAsync(new StudyPoolFilter(DueBefore: Now, Sort: StudyPoolSort.DueAsc, Limit: 200));

        var best = long.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var watch = Stopwatch.StartNew();
            var pool = await repository.GetStudyCandidatesAsync(
                new StudyPoolFilter(DueBefore: Now, Sort: StudyPoolSort.DueAsc, Limit: 200));
            watch.Stop();

            Assert.Equal(200, pool.Count);
            best = Math.Min(best, watch.ElapsedMilliseconds);
        }

        Assert.True(best < 200, $"очередь дня на 5000 карточек заняла {best} мс");
    }

    [Fact]
    public async Task A_whole_session_worth_of_answers_lands_without_losing_anything()
    {
        // Сессия на две сотни карточек: каждый ответ — своя запись, и все двести обязаны доехать.
        // Заодно проходит путь, на котором триггер Cards_au пересобирает строку поискового индекса
        // (он объявлен без списка колонок, так что срабатывает и на запись одного лишь срока).
        // Времени тест не меряет намеренно: классы тестов идут параллельно, и секундомер ловил бы
        // соседей, а не регрессию — числа замера лежат в CURRENT.md.
        var cards = Enumerable.Range(0, 200)
            .Select(i => TestData.Card(front: $"карточка {i}"))
            .ToArray();

        await SeedAsync(cards: cards);

        var repository = new CardRepository(Factory);
        var outcome = new ReviewOutcome(6, 2.5, 2, 0, Now.AddDays(6), false);

        foreach (var card in cards)
        {
            await repository.ApplyReviewAsync(card.Id, outcome, Now);
        }

        await using var context = CreateContext();

        Assert.Equal(200, context.Cards.Count(c => c.Repetitions == 2));
        Assert.Equal(200, context.Cards.Count(c => c.DueAt != null));
        Assert.All(context.Cards.ToList(), c => Assert.Equal(6, c.IntervalDays, 6));
    }

    // --- вспомогательное -------------------------------------------------------------------------------------------

    private async Task SeedAsync(
        Subject[]? subjects = null,
        CardDeck[]? decks = null,
        Card[]? cards = null,
        CardTag[]? tags = null,
        (Guid CardId, Guid TagId)[]? links = null)
    {
        await using var context = CreateContext();

        if (subjects is { Length: > 0 })
        {
            context.Subjects.AddRange(subjects);
        }

        if (decks is { Length: > 0 })
        {
            context.CardDecks.AddRange(decks);
        }

        if (cards is { Length: > 0 })
        {
            context.Cards.AddRange(cards);
        }

        if (tags is { Length: > 0 })
        {
            context.CardTags.AddRange(tags);
        }

        if (links is { Length: > 0 })
        {
            context.CardTagLinks.AddRange(links.Select(l => new CardTagLink { CardId = l.CardId, TagId = l.TagId }));
        }

        await context.SaveChangesAsync();
    }
}
