using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты <see cref="NextPairTimeCalculator"/> (new_addons.md §5.1): подсказка времени новой
/// пары от конца последней пары этого дня + перерыв, с учётом обеденного окна.
/// </summary>
public sealed class NextPairTimeCalculatorTests
{
    private static readonly TimeSpan PairDuration = TimeSpan.FromMinutes(90);
    private static readonly TimeSpan Break = TimeSpan.FromMinutes(10);

    private static ScheduleEntry Entry(int startHour, int startMinute, int durationMinutes) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = Guid.NewGuid(),
        DayOfWeek = DayOfWeek.Monday,
        StartTime = new TimeOnly(startHour, startMinute),
        EndTime = new TimeOnly(startHour, startMinute).AddMinutes(durationMinutes),
    };

    [Fact]
    public void No_entries_that_day_falls_back_to_the_given_start()
    {
        var fallback = new TimeOnly(8, 0);

        var suggestion = NextPairTimeCalculator.Suggest([], PairDuration, Break, null, fallback);

        Assert.False(suggestion.BasedOnPreviousPair);
        Assert.Equal(fallback, suggestion.Start);
        Assert.Equal(fallback.Add(PairDuration), suggestion.End);
    }

    [Fact]
    public void Existing_pair_suggests_its_end_plus_break()
    {
        // Предыдущая закончилась в 11:40 + перерыв 10 мин = 11:50 (пример из new_addons.md §5.1).
        var entries = new[] { Entry(10, 10, 90) };

        var suggestion = NextPairTimeCalculator.Suggest(
            entries, PairDuration, Break, null, new TimeOnly(9, 0));

        Assert.True(suggestion.BasedOnPreviousPair);
        Assert.Equal(new TimeOnly(11, 50), suggestion.Start);
        Assert.Equal(new TimeOnly(13, 20), suggestion.End);
    }

    [Fact]
    public void Multiple_entries_use_the_latest_end_not_the_last_added()
    {
        var entries = new[]
        {
            Entry(13, 0, 90),  // заканчивается позже всех
            Entry(9, 0, 90),
            Entry(10, 40, 90),
        };

        var suggestion = NextPairTimeCalculator.Suggest(
            entries, PairDuration, Break, null, new TimeOnly(8, 0));

        Assert.Equal(new TimeOnly(14, 40), suggestion.Start); // 14:30 + 10 мин
    }

    [Fact]
    public void Computed_start_inside_lunch_window_shifts_to_lunch_end()
    {
        var entries = new[] { Entry(12, 0, 90) }; // конец 13:30, +10 мин = 13:40 — попадает в обед
        var lunch = (Start: new TimeOnly(13, 20), End: new TimeOnly(13, 50));

        var suggestion = NextPairTimeCalculator.Suggest(
            entries, PairDuration, Break, lunch, new TimeOnly(9, 0));

        Assert.Equal(lunch.End, suggestion.Start);
        Assert.Equal(lunch.End.Add(PairDuration), suggestion.End);
    }

    [Fact]
    public void Computed_start_outside_lunch_window_is_not_shifted()
    {
        var entries = new[] { Entry(8, 0, 90) }; // конец 09:30, +10 мин = 09:40 — до обеда
        var lunch = (Start: new TimeOnly(13, 20), End: new TimeOnly(13, 50));

        var suggestion = NextPairTimeCalculator.Suggest(
            entries, PairDuration, Break, lunch, new TimeOnly(9, 0));

        Assert.Equal(new TimeOnly(9, 40), suggestion.Start);
    }

    [Fact]
    public void Null_lunch_break_does_not_break_the_calculation()
    {
        var entries = new[] { Entry(13, 10, 90) }; // конец 14:40, +10 мин = 14:50

        var suggestion = NextPairTimeCalculator.Suggest(
            entries, PairDuration, Break, null, new TimeOnly(9, 0));

        Assert.Equal(new TimeOnly(14, 50), suggestion.Start);
    }

    [Fact]
    public void LunchOf_reads_both_fields_or_returns_null()
    {
        Assert.Null(NextPairTimeCalculator.LunchOf(null));
        Assert.Null(NextPairTimeCalculator.LunchOf(new Semester()));

        var semester = new Semester
        {
            LunchBreakStart = new TimeOnly(13, 20),
            LunchBreakEnd = new TimeOnly(13, 50),
        };

        Assert.Equal((new TimeOnly(13, 20), new TimeOnly(13, 50)), NextPairTimeCalculator.LunchOf(semester));
    }
}
