using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Тесты границы «учебных суток» (new_addons.md §6.3, Phase 12.8): ответ в час ночи должен считаться
/// вчерашним вечером, иначе полуночная сессия обнуляет дневные лимиты посреди работы.
/// </summary>
public sealed class StudyDayTests
{
    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 9, day, hour, minute, 0, TimeSpan.FromHours(3));

    [Theory]
    [InlineData(7, 1, 6)]     // час ночи — это ещё вчерашний день
    [InlineData(7, 3, 6)]
    [InlineData(7, 4, 7)]     // ровно граница — уже новый день
    [InlineData(7, 12, 7)]
    [InlineData(7, 23, 7)]
    public void The_rollover_hour_decides_which_day_an_answer_belongs_to(int day, int hour, int expectedDay)
    {
        Assert.Equal(new DateOnly(2026, 9, expectedDay), StudyDay.DayOf(At(day, hour), 4));
    }

    [Fact]
    public void The_start_of_the_day_carries_the_original_offset()
    {
        var start = StudyDay.StartOf(At(7, 1), 4);

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.FromHours(3)), start);
    }

    [Fact]
    public void A_zero_rollover_hour_means_plain_midnight()
    {
        Assert.Equal(new DateOnly(2026, 9, 7), StudyDay.DayOf(At(7, 0, 1), 0));
        Assert.Equal(new DateOnly(2026, 9, 7), StudyDay.DayOf(At(7, 23), 0));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(99)]
    public void A_nonsense_rollover_hour_is_clamped_instead_of_throwing(int hour)
    {
        var day = StudyDay.DayOf(At(7, 12), hour);

        Assert.InRange(day, new DateOnly(2026, 9, 6), new DateOnly(2026, 9, 7));
    }

    [Fact]
    public void Consecutive_answers_around_the_boundary_land_in_different_days()
    {
        Assert.NotEqual(StudyDay.DayOf(At(7, 3, 59), 4), StudyDay.DayOf(At(7, 4, 0), 4));
    }
}
