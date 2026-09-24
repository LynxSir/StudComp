using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Очередь дня (new_addons.md §6.3, Phase 12.8): что в неё попадает, в каком порядке и сколько
/// отсекают дневные лимиты.
/// </summary>
public sealed class ReviewQueueServiceTests : CardsDatabaseTestBase
{
    [Fact]
    public async Task Only_ripe_cards_get_into_the_queue()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);
        await SeedReviewedCardAsync("Ещё рано", dueInDays: 3);

        var snapshot = (await Queue.BuildAsync()).Value;

        Assert.Single(snapshot.CardIds);
        Assert.Equal(1, snapshot.DueCount);
    }

    [Fact]
    public async Task Suspended_and_deleted_cards_never_reach_the_queue()
    {
        var suspended = await SeedReviewedCardAsync("Отложена", dueInDays: -1);
        var trashed = await SeedReviewedCardAsync("В корзине", dueInDays: -1);
        await SeedReviewedCardAsync("Обычная", dueInDays: -1);

        await Cards.SetSuspendedAsync(suspended, true);
        await Cards.MoveToTrashAsync([trashed]);

        var snapshot = (await Queue.BuildAsync()).Value;

        Assert.Single(snapshot.CardIds);
        Assert.DoesNotContain(suspended, snapshot.CardIds);
        Assert.DoesNotContain(trashed, snapshot.CardIds);
    }

    [Fact]
    public async Task The_most_overdue_cards_come_first()
    {
        var fresh = await SeedReviewedCardAsync("Вчера", dueInDays: -1);
        var stale = await SeedReviewedCardAsync("Две недели назад", dueInDays: -14);
        var middle = await SeedReviewedCardAsync("Три дня назад", dueInDays: -3);

        var snapshot = (await Queue.BuildAsync()).Value;

        Assert.Equal([stale, middle, fresh], snapshot.CardIds);
    }

    [Fact]
    public async Task The_daily_review_limit_cuts_the_queue_and_says_so()
    {
        for (var i = 0; i < 10; i++)
        {
            await SeedReviewedCardAsync($"Карточка {i}", dueInDays: -(i + 1));
        }

        Options.CurrentValue = new CardsOptionsBuilder().WithLimits(0, 4).Build();

        var snapshot = (await Queue.BuildAsync()).Value;

        Assert.Equal(4, snapshot.CardIds.Count);

        // Молча спрятать шесть карточек хуже, чем честно сказать о них.
        Assert.Equal(6, snapshot.SkippedByLimit);
    }

    [Fact]
    public async Task New_cards_are_mixed_in_under_their_own_limit()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedReviewedCardAsync($"Повторение {i}", dueInDays: -(i + 1));
            await SeedCardAsync($"Новая {i}");
        }

        Options.CurrentValue = new CardsOptionsBuilder().WithLimits(2, 100).WithNewCardShare(0.3).Build();

        var snapshot = (await Queue.BuildAsync()).Value;

        Assert.Equal(2, snapshot.NewCount);
        Assert.Equal(5, snapshot.DueCount);
        Assert.Equal(7, snapshot.CardIds.Count);
    }

    [Fact]
    public async Task Cards_already_seen_today_shrink_the_remaining_budget()
    {
        var id = await SeedReviewedCardAsync("Первая", dueInDays: -1);
        await SeedReviewedCardAsync("Вторая", dueInDays: -2);

        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Review, StudySessionFilter.Empty))).Value;
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 500));

        Options.CurrentValue = new CardsOptionsBuilder().WithLimits(20, 2).Build();

        var counts = await Queue.GetCountsAsync();

        Assert.Equal(1, counts.ReviewsRemainingToday);
    }

    [Fact]
    public async Task Counts_do_not_require_building_the_queue()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);
        await SeedCardAsync("Новая");

        var counts = await Queue.GetCountsAsync();

        Assert.Equal(1, counts.Due);
        Assert.Equal(1, counts.New);
        Assert.Equal(20, counts.NewRemainingToday);
        Assert.Equal(200, counts.ReviewsRemainingToday);
    }

    [Fact]
    public async Task A_disabled_review_refuses_to_build_a_queue()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);

        Options.CurrentValue = new CardsOptionsBuilder().WithReviewEnabled(false).Build();

        var result = await Queue.BuildAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("cards.review_disabled", result.Error.Code);
    }

    [Fact]
    public async Task An_empty_queue_tells_when_the_next_one_comes()
    {
        await SeedReviewedCardAsync("Завтра", dueInDays: 1);

        var snapshot = (await Queue.BuildAsync()).Value;

        // Пустая очередь — это «на сегодня всё, следующее повторение завтра», а не «нет данных».
        Assert.Empty(snapshot.CardIds);
        Assert.NotNull(snapshot.NextDueAt);
        Assert.True(snapshot.NextDueAt > DateTimeOffset.Now);
    }

    [Fact]
    public async Task An_empty_card_index_gives_an_empty_queue_without_a_next_date()
    {
        var snapshot = (await Queue.BuildAsync()).Value;

        Assert.Empty(snapshot.CardIds);
        Assert.Null(snapshot.NextDueAt);
    }

    [Fact]
    public async Task The_queue_can_be_narrowed_to_one_subject()
    {
        var math = await SeedSubjectAsync("Матан", "МА");
        var physics = await SeedSubjectAsync("Физика", "ФИ");

        var mathCard = await SeedReviewedCardAsync("Матан", dueInDays: -1, subjectId: math);
        await SeedReviewedCardAsync("Физика", dueInDays: -1, subjectId: physics);

        var snapshot = (await Queue.BuildAsync(math)).Value;

        Assert.Equal([mathCard], snapshot.CardIds);
    }

    [Fact]
    public async Task The_forecast_shows_the_load_of_the_coming_days()
    {
        await SeedReviewedCardAsync("Завтра", dueInDays: 1);
        await SeedReviewedCardAsync("Тоже завтра", dueInDays: 1);
        await SeedReviewedCardAsync("Через неделю", dueInDays: 7);

        var forecast = await Queue.GetForecastAsync(30);

        Assert.Equal(2, forecast.Count);
        Assert.Equal(2, forecast[0].Count);
        Assert.Equal(1, forecast[1].Count);
    }

    [Fact]
    public async Task The_queue_order_follows_the_setting()
    {
        var older = await SeedReviewedCardAsync("Старая", dueInDays: -1);
        var newer = await SeedReviewedCardAsync("Новая", dueInDays: -10);

        Options.CurrentValue = new CardsOptionsBuilder().WithQueueOrder(StudyOrder.NewestFirst).Build();

        var snapshot = (await Queue.BuildAsync()).Value;

        Assert.Equal([newer, older], snapshot.CardIds);
    }
}
