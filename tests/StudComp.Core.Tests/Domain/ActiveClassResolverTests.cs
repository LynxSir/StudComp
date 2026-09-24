using System.Globalization;
using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты <see cref="ActiveClassResolver"/> (new_addons.md §1.8). Точка отсчёта —
/// <c>2025-09-01</c> (понедельник, неделя 1 = нечётная при <c>firstWeekIsOdd = true</c>).
/// </summary>
public sealed class ActiveClassResolverTests
{
    private static DateOnly SemStart => DateOnly.Parse("2025-09-01", CultureInfo.InvariantCulture);

    private static DateTimeOffset At(string iso) =>
        new(DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), TimeSpan.Zero);

    private static ScheduleEntry Entry(DayOfWeek day, string start, string end, WeekParity parity = WeekParity.Any) =>
        new()
        {
            Id = Guid.NewGuid(),
            SubjectId = Guid.NewGuid(),
            DayOfWeek = day,
            StartTime = TimeOnly.Parse(start, CultureInfo.InvariantCulture),
            EndTime = TimeOnly.Parse(end, CultureInfo.InvariantCulture),
            WeekParity = parity,
        };

    [Fact]
    public void Empty_schedule_yields_nothing()
    {
        Assert.Null(ActiveClassResolver.Resolve([], At("2025-09-01T10:00"), SemStart));
    }

    [Fact]
    public void Class_in_progress_is_reported_as_on_now()
    {
        var entry = Entry(DayOfWeek.Monday, "10:00", "11:30");

        var result = ActiveClassResolver.Resolve([entry], At("2025-09-01T10:30"), SemStart);

        Assert.NotNull(result);
        Assert.True(result!.Value.IsOnNow);
        Assert.Equal(entry.Id, result.Value.Entry.Id);
        Assert.Equal(TimeSpan.FromMinutes(60), result.Value.EndsIn);
    }

    [Fact]
    public void Between_classes_returns_the_next_one_today()
    {
        var earlier = Entry(DayOfWeek.Monday, "08:00", "09:30");
        var later = Entry(DayOfWeek.Monday, "12:00", "13:30");

        var result = ActiveClassResolver.Resolve([earlier, later], At("2025-09-01T10:00"), SemStart);

        Assert.NotNull(result);
        Assert.False(result!.Value.IsOnNow);
        Assert.Equal(later.Id, result.Value.Entry.Id);
        Assert.Equal(TimeSpan.FromHours(2), result.Value.StartsIn);
    }

    [Fact]
    public void Running_class_wins_over_a_later_one_today()
    {
        var running = Entry(DayOfWeek.Monday, "10:00", "11:30");
        var later = Entry(DayOfWeek.Monday, "12:00", "13:30");

        var result = ActiveClassResolver.Resolve([later, running], At("2025-09-01T10:15"), SemStart);

        Assert.Equal(running.Id, result!.Value.Entry.Id);
        Assert.True(result.Value.IsOnNow);
    }

    [Fact]
    public void Odd_week_skips_an_even_only_class_and_falls_through_to_a_later_day()
    {
        var evenOnly = Entry(DayOfWeek.Monday, "10:00", "11:30", WeekParity.Even);
        var tuesday = Entry(DayOfWeek.Tuesday, "09:00", "10:30");

        var result = ActiveClassResolver.Resolve([evenOnly, tuesday], At("2025-09-01T09:00"), SemStart);

        Assert.Equal(tuesday.Id, result!.Value.Entry.Id);
        Assert.False(result.Value.IsOnNow);
        Assert.True(result.Value.StartsIn > TimeSpan.FromHours(20));
    }

    [Fact]
    public void Without_semester_start_parity_is_ignored()
    {
        var evenOnly = Entry(DayOfWeek.Monday, "10:00", "11:30", WeekParity.Even);

        var result = ActiveClassResolver.Resolve([evenOnly], At("2025-09-01T10:30"), semesterStart: null);

        Assert.NotNull(result);
        Assert.True(result!.Value.IsOnNow);
    }

    [Fact]
    public void Nothing_today_returns_the_nearest_class_on_a_following_day()
    {
        var wednesday = Entry(DayOfWeek.Wednesday, "09:00", "10:30");

        var result = ActiveClassResolver.Resolve([wednesday], At("2025-09-01T12:00"), SemStart);

        Assert.Equal(wednesday.Id, result!.Value.Entry.Id);
        Assert.False(result.Value.IsOnNow);
        // Со среды 09:00 минус понедельник 12:00 — чуть меньше двух суток.
        Assert.InRange(result.Value.StartsIn, TimeSpan.FromDays(1), TimeSpan.FromDays(2));
    }

    [Fact]
    public void Earliest_of_several_upcoming_classes_wins()
    {
        var lateToday = Entry(DayOfWeek.Monday, "18:00", "19:30");
        var earlyToday = Entry(DayOfWeek.Monday, "15:00", "16:30");

        var result = ActiveClassResolver.Resolve([lateToday, earlyToday], At("2025-09-01T12:00"), SemStart);

        Assert.Equal(earlyToday.Id, result!.Value.Entry.Id);
    }
}
