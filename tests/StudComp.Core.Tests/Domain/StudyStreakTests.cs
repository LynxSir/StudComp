using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Тесты серии дней подряд (new_addons.md §6.5, Phase 12.8) — одна строка на вкладке «Статистика»,
/// без значков и салютов.
/// </summary>
public sealed class StudyStreakTests
{
    private static readonly DateOnly Today = new(2026, 9, 7);

    private static DateOnly Day(int daysAgo) => Today.AddDays(-daysAgo);

    [Fact]
    public void An_empty_history_has_no_streak()
    {
        var streak = StudyStreak.Calculate([], Today);

        Assert.Equal(StudyStreakInfo.Empty, streak);
    }

    [Fact]
    public void A_streak_ending_today_is_alive()
    {
        var streak = StudyStreak.Calculate([Day(2), Day(1), Day(0)], Today);

        Assert.Equal(3, streak.Current);
        Assert.Equal(3, streak.Longest);
        Assert.Equal(Today, streak.LastActiveDay);
    }

    [Fact]
    public void A_streak_ending_yesterday_is_still_alive()
    {
        // Иначе строка «серия 12 дней» обнулялась бы каждое утро — ровно тогда, когда должна мотивировать.
        var streak = StudyStreak.Calculate([Day(3), Day(2), Day(1)], Today);

        Assert.Equal(3, streak.Current);
    }

    [Fact]
    public void A_gap_of_two_days_breaks_the_streak()
    {
        var streak = StudyStreak.Calculate([Day(4), Day(3), Day(2)], Today);

        Assert.Equal(0, streak.Current);
        Assert.Equal(3, streak.Longest);
    }

    [Fact]
    public void The_longest_streak_survives_a_break()
    {
        List<DateOnly> days = [Day(20), Day(19), Day(18), Day(17), Day(16), Day(1), Day(0)];

        var streak = StudyStreak.Calculate(days, Today);

        Assert.Equal(2, streak.Current);
        Assert.Equal(5, streak.Longest);
    }

    [Fact]
    public void Duplicate_days_do_not_inflate_the_streak()
    {
        List<DateOnly> days = [Day(1), Day(1), Day(1), Day(0), Day(0)];

        var streak = StudyStreak.Calculate(days, Today);

        Assert.Equal(2, streak.Current);
    }

    [Fact]
    public void Unsorted_input_is_handled()
    {
        var streak = StudyStreak.Calculate([Day(0), Day(2), Day(1)], Today);

        Assert.Equal(3, streak.Current);
    }

    [Fact]
    public void A_single_day_is_a_streak_of_one()
    {
        var streak = StudyStreak.Calculate([Today], Today);

        Assert.Equal(1, streak.Current);
        Assert.Equal(1, streak.Longest);
    }

    [Fact]
    public void A_day_from_the_future_does_not_break_the_streak()
    {
        // Сбитые часы или импорт чужой истории не повод обнулять серию.
        var streak = StudyStreak.Calculate([Day(0), Day(-1)], Today);

        Assert.Equal(2, streak.Current);
    }
}
