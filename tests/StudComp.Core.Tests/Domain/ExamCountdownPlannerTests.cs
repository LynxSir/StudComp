using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты обратного отсчёта к экзамену (new_addons.md §6.4, Phase 12.8). Главное свойство
/// планировщика — честность: если K показов не помещается в оставшиеся дни, он так и говорит.
/// </summary>
public sealed class ExamCountdownPlannerTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Guid Id(int index) => Guid.Parse($"00000000-0000-0000-0000-{index:D12}");

    private static StudyCandidate Card(int index, int lapses = 0, double ease = 2.5) =>
        new(Id(index), null, null, 0, lapses, ease, null, null, Epoch);

    private static List<StudyCandidate> Pool(int count) =>
        [.. Enumerable.Range(0, count).Select(i => Card(i))];

    // --- достижимость -----------------------------------------------------------------------------

    [Fact]
    public void A_comfortable_deadline_is_achievable()
    {
        var plan = ExamCountdownPlanner.Plan(new CramPlanRequest(CardCount: 100, DaysLeft: 9, MinShowsPerCard: 3));

        Assert.True(plan.IsAchievable);
        Assert.Equal(300, plan.TotalShowsNeeded);
        Assert.Equal(34, plan.CardsToday);           // 300 / 9, округление вверх
        Assert.Equal(3, plan.AchievableShowsPerCard);
    }

    [Fact]
    public void A_tight_deadline_reports_how_many_shows_actually_fit()
    {
        // Никакого «всё по плану»: 100 карточек за 2 дня по 3 раза не выйдет, и план это говорит.
        var plan = ExamCountdownPlanner.Plan(
            new CramPlanRequest(CardCount: 100, DaysLeft: 2, MinShowsPerCard: 3, MaxCardsPerDay: 100));

        Assert.False(plan.IsAchievable);
        Assert.Equal(2, plan.AchievableShowsPerCard);
        Assert.Equal(3, plan.RequestedShowsPerCard);
    }

    [Fact]
    public void A_daily_cap_can_make_the_goal_unreachable()
    {
        var plan = ExamCountdownPlanner.Plan(
            new CramPlanRequest(CardCount: 100, DaysLeft: 10, MinShowsPerCard: 3, MaxCardsPerDay: 20));

        Assert.False(plan.IsAchievable);
        Assert.Equal(2, plan.AchievableShowsPerCard);
        Assert.Equal(20, plan.CardsToday);
    }

    [Fact]
    public void A_generous_cap_does_not_dump_the_whole_material_on_today()
    {
        // Потолок дня отвечает за достижимость, а сегодняшняя порция — за ровную раскладку остатка.
        var plan = ExamCountdownPlanner.Plan(
            new CramPlanRequest(CardCount: 100, DaysLeft: 9, MinShowsPerCard: 3, MaxCardsPerDay: 200));

        Assert.Equal(34, plan.CardsToday);
        Assert.True(plan.IsAchievable);
    }

    [Fact]
    public void The_last_day_still_counts_as_a_whole_day()
    {
        var plan = ExamCountdownPlanner.Plan(new CramPlanRequest(CardCount: 20, DaysLeft: 0, MinShowsPerCard: 2));

        Assert.False(plan.IsExpired);
        Assert.Equal(40, plan.CardsToday);
    }

    [Fact]
    public void A_passed_exam_asks_for_nothing()
    {
        var plan = ExamCountdownPlanner.Plan(new CramPlanRequest(CardCount: 20, DaysLeft: -1, MinShowsPerCard: 2));

        Assert.True(plan.IsExpired);
        Assert.False(plan.IsAchievable);
        Assert.Equal(0, plan.CardsToday);
    }

    [Fact]
    public void An_empty_deck_is_not_a_division_by_zero()
    {
        var plan = ExamCountdownPlanner.Plan(new CramPlanRequest(CardCount: 0, DaysLeft: 5, MinShowsPerCard: 3));

        Assert.Equal(0, plan.TotalShowsNeeded);
        Assert.Equal(0, plan.CardsToday);
        Assert.Equal(100, plan.ProgressPercent);
        Assert.True(plan.IsAchievable);
    }

    [Fact]
    public void Zero_requested_shows_is_read_as_one()
    {
        var plan = ExamCountdownPlanner.Plan(new CramPlanRequest(CardCount: 10, DaysLeft: 5, MinShowsPerCard: 0));

        Assert.Equal(1, plan.RequestedShowsPerCard);
        Assert.Equal(10, plan.TotalShowsNeeded);
    }

    // --- прогресс ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(150, 50)]
    [InlineData(300, 100)]
    [InlineData(999, 100)]      // больше, чем нужно — зажимается
    public void Progress_is_a_percentage_of_the_whole_plan(int done, int expected)
    {
        var plan = ExamCountdownPlanner.Plan(
            new CramPlanRequest(CardCount: 100, DaysLeft: 9, MinShowsPerCard: 3, ShowsDone: done));

        Assert.Equal(expected, plan.ProgressPercent);
    }

    [Fact]
    public void Work_already_done_shrinks_todays_portion()
    {
        // Остаток размазывается по оставшимся дням, а не сваливается на сегодня.
        var plan = ExamCountdownPlanner.Plan(
            new CramPlanRequest(CardCount: 100, DaysLeft: 9, MinShowsPerCard: 3, ShowsDone: 290));

        Assert.Equal(2, plan.CardsToday);
    }

    [Fact]
    public void On_the_last_day_the_whole_remainder_lands_on_today()
    {
        var plan = ExamCountdownPlanner.Plan(
            new CramPlanRequest(CardCount: 100, DaysLeft: 1, MinShowsPerCard: 3, ShowsDone: 290));

        Assert.Equal(10, plan.CardsToday);
    }

    // --- очередь показов -----------------------------------------------------------------------------

    [Fact]
    public void The_cycle_shows_every_card_exactly_k_times()
    {
        var cycle = ExamCountdownPlanner.BuildCycle(Pool(20), shows: 3, seed: 1);

        Assert.Equal(60, cycle.Count);
        Assert.All(cycle.GroupBy(id => id), group => Assert.Equal(3, group.Count()));
    }

    [Fact]
    public void The_hardest_cards_come_first_in_the_opening_pass()
    {
        List<StudyCandidate> pool =
        [
            Card(1, lapses: 0),
            Card(2, lapses: 7),
            Card(3, lapses: 3),
        ];

        var cycle = ExamCountdownPlanner.BuildCycle(pool, shows: 1, seed: 1);

        Assert.Equal([Id(2), Id(3), Id(1)], cycle);
    }

    [Fact]
    public void Later_passes_are_not_a_carbon_copy_of_the_first()
    {
        // Иначе второй круг заучивается как порядок, а не как материал.
        var cycle = ExamCountdownPlanner.BuildCycle(Pool(20), shows: 2, seed: 42);

        var first = cycle.Take(20).ToList();
        var second = cycle.Skip(20).Take(20).ToList();

        Assert.NotEqual(first, second);
        Assert.Equal(first.OrderBy(x => x), second.OrderBy(x => x));
    }

    [Fact]
    public void The_cycle_is_reproducible_from_the_seed()
    {
        var pool = Pool(15);

        Assert.Equal(
            ExamCountdownPlanner.BuildCycle(pool, 3, 99),
            ExamCountdownPlanner.BuildCycle(pool, 3, 99));
    }

    [Fact]
    public void An_empty_pool_gives_an_empty_cycle()
    {
        Assert.Empty(ExamCountdownPlanner.BuildCycle([], 3, 1));
    }

    // --- срез на сегодня -------------------------------------------------------------------------------

    [Fact]
    public void Slices_neither_lose_nor_duplicate_shows()
    {
        var cycle = ExamCountdownPlanner.BuildCycle(Pool(10), shows: 3, seed: 5);
        var collected = new List<Guid>();

        for (var offset = 0; offset < cycle.Count; offset += 7)
        {
            collected.AddRange(ExamCountdownPlanner.Slice(cycle, offset, 7));
        }

        Assert.Equal(cycle, collected);
    }

    [Fact]
    public void A_slice_beyond_the_end_is_empty_rather_than_an_error()
    {
        var cycle = ExamCountdownPlanner.BuildCycle(Pool(3), shows: 1, seed: 1);

        Assert.Empty(ExamCountdownPlanner.Slice(cycle, 100, 5));
        Assert.Empty(ExamCountdownPlanner.Slice(cycle, 0, 0));
        Assert.Empty(ExamCountdownPlanner.Slice([], 0, 5));
    }

    [Fact]
    public void A_slice_is_clipped_to_what_is_left()
    {
        var cycle = ExamCountdownPlanner.BuildCycle(Pool(4), shows: 1, seed: 1);

        Assert.Equal(2, ExamCountdownPlanner.Slice(cycle, 2, 10).Count);
    }
}
