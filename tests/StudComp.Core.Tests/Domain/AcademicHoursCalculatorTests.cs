using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Расчёт ориентировочных часов за семестр (new_addons.md §5). Границы интервала включающие,
/// чётность считает <see cref="WeekParityCalculator"/>, часы — астрономические.
/// </summary>
public class AcademicHoursCalculatorTests
{
    // 2026-09-01 — вторник. Удобная точка: старт семестра приходится не на понедельник,
    // что и ловит большинство ошибок на единицу.
    private static readonly DateOnly Start = new(2026, 9, 1);

    private static ScheduleEntry Entry(
        DayOfWeek day,
        WeekParity parity = WeekParity.Any,
        ScheduleEntryType type = ScheduleEntryType.Lecture,
        int startHour = 9,
        int durationMinutes = 90) => new()
        {
            Id = Guid.NewGuid(),
            SubjectId = Guid.NewGuid(),
            DayOfWeek = day,
            StartTime = new TimeOnly(startHour, 0),
            EndTime = new TimeOnly(startHour, 0).AddMinutes(durationMinutes),
            Type = type,
            WeekParity = parity,
        };

    [Fact]
    public void Weekly_class_repeats_once_per_week()
    {
        // Вторник, ровно 16 недель: 01.09 + 15*7 = 15.12.
        var end = Start.AddDays(15 * 7);

        var count = AcademicHoursCalculator.CountOccurrences(Entry(DayOfWeek.Tuesday), Start, end);

        Assert.Equal(16, count);
    }

    [Fact]
    public void Class_on_semester_start_day_is_counted()
    {
        var count = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday), Start, Start);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Class_before_semester_start_within_first_week_is_skipped()
    {
        // Понедельник 31.08 — до старта, поэтому первое проведение только 07.09.
        var end = new DateOnly(2026, 9, 8);

        var count = AcademicHoursCalculator.CountOccurrences(Entry(DayOfWeek.Monday), Start, end);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Class_exactly_on_semester_end_is_counted()
    {
        var end = new DateOnly(2026, 9, 8); // вторник

        var count = AcademicHoursCalculator.CountOccurrences(Entry(DayOfWeek.Tuesday), Start, end);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Class_one_day_after_semester_end_is_not_counted()
    {
        var end = new DateOnly(2026, 9, 7); // понедельник, вторник уже вне интервала

        var count = AcademicHoursCalculator.CountOccurrences(Entry(DayOfWeek.Tuesday), Start, end);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Empty_interval_gives_zero()
    {
        var count = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday), Start, Start.AddDays(-1));

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(16)]
    public void Odd_and_even_counts_sum_to_the_weekly_count(int weeks)
    {
        var end = Start.AddDays(weeks * 7);

        var any = AcademicHoursCalculator.CountOccurrences(Entry(DayOfWeek.Thursday), Start, end);
        var odd = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Thursday, WeekParity.Odd), Start, end);
        var even = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Thursday, WeekParity.Even), Start, end);

        Assert.Equal(any, odd + even);
    }

    [Fact]
    public void Odd_weeks_get_the_extra_class_when_first_week_is_odd()
    {
        // 5 недель: недели 1,3,5 — нечётные, 2 и 4 — чётные.
        var end = Start.AddDays(4 * 7);

        var odd = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday, WeekParity.Odd), Start, end);
        var even = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday, WeekParity.Even), Start, end);

        Assert.Equal(3, odd);
        Assert.Equal(2, even);
    }

    [Fact]
    public void First_week_even_mirrors_the_parity_counts()
    {
        var end = Start.AddDays(4 * 7);

        var odd = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday, WeekParity.Odd), Start, end, firstWeekIsOdd: false);
        var even = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday, WeekParity.Even), Start, end, firstWeekIsOdd: false);

        Assert.Equal(2, odd);
        Assert.Equal(3, even);
    }

    [Fact]
    public void Sunday_class_follows_the_monday_based_week_convention()
    {
        // Воскресенье 06.09 относится к неделе, начавшейся в понедельник 31.08, — то есть к первой
        // (нечётной) неделе, как и сам старт семестра во вторник 01.09.
        var end = new DateOnly(2026, 9, 6);

        var odd = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Sunday, WeekParity.Odd), Start, end);
        var even = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Sunday, WeekParity.Even), Start, end);

        Assert.Equal(1, odd);
        Assert.Equal(0, even);
    }

    [Fact]
    public void Held_and_remaining_split_at_as_of_date()
    {
        var end = Start.AddDays(15 * 7);
        var asOf = Start.AddDays(5 * 7); // прошло 5 проведений

        var result = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday), Start, end, asOf);

        Assert.Equal(16, result.Total);
        Assert.Equal(5, result.Held);
        Assert.Equal(11, result.Remaining);
        Assert.Equal(result.Total, result.Held + result.Remaining);
    }

    [Fact]
    public void Class_on_as_of_date_counts_as_remaining()
    {
        var result = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday), Start, Start, asOf: Start);

        Assert.Equal(0, result.Held);
        Assert.Equal(1, result.Remaining);
    }

    [Fact]
    public void As_of_before_semester_leaves_everything_ahead()
    {
        var end = Start.AddDays(15 * 7);

        var result = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday), Start, end, asOf: Start.AddDays(-30));

        Assert.Equal(0, result.Held);
        Assert.Equal(result.Total, result.Remaining);
    }

    [Fact]
    public void As_of_after_semester_leaves_nothing_ahead()
    {
        var end = Start.AddDays(15 * 7);

        var result = AcademicHoursCalculator.CountOccurrences(
            Entry(DayOfWeek.Tuesday), Start, end, asOf: end.AddDays(30));

        Assert.Equal(result.Total, result.Held);
        Assert.Equal(0, result.Remaining);
    }

    [Fact]
    public void Next_occurrence_returns_todays_class_and_null_after_semester()
    {
        var end = Start.AddDays(15 * 7);
        var entry = Entry(DayOfWeek.Tuesday);

        Assert.Equal(Start, AcademicHoursCalculator.NextOccurrence(entry, Start, end, Start));
        Assert.Null(AcademicHoursCalculator.NextOccurrence(entry, Start, end, end.AddDays(1)));
    }

    [Fact]
    public void Summarize_splits_hours_by_class_type()
    {
        var end = Start.AddDays(15 * 7); // 16 недель

        var summary = AcademicHoursCalculator.Summarize(
            [
                Entry(DayOfWeek.Tuesday, type: ScheduleEntryType.Lecture),
                Entry(DayOfWeek.Thursday, WeekParity.Odd, ScheduleEntryType.Lab),
            ],
            Start, end, asOf: Start);

        var lecture = summary.ByType.Single(x => x.Type == ScheduleEntryType.Lecture);
        var lab = summary.ByType.Single(x => x.Type == ScheduleEntryType.Lab);

        Assert.Equal(16, lecture.Classes);
        Assert.Equal(24d, lecture.TotalHours, 3);   // 16 пар × 1,5 ч
        Assert.Equal(8, lab.Classes);
        Assert.Equal(12d, lab.TotalHours, 3);
        Assert.Equal(24, summary.TotalClasses);
        Assert.Equal(36d, summary.TotalHours, 3);
        Assert.Equal(0d, summary.HeldHours, 3);
        Assert.Equal(Start, summary.NextClassDate);
    }

    [Fact]
    public void Summarize_of_empty_schedule_is_all_zeros()
    {
        var summary = AcademicHoursCalculator.Summarize(
            [], Start, Start.AddDays(100), asOf: Start);

        Assert.Empty(summary.ByType);
        Assert.Equal(0, summary.TotalClasses);
        Assert.Equal(0d, summary.TotalHours, 3);
        Assert.Null(summary.NextClassDate);
    }

    [Fact]
    public void Lesson_with_broken_time_range_contributes_no_hours()
    {
        var broken = Entry(DayOfWeek.Tuesday);
        broken.EndTime = broken.StartTime;

        var summary = AcademicHoursCalculator.Summarize(
            [broken], Start, Start.AddDays(15 * 7), asOf: Start);

        Assert.Equal(16, summary.TotalClasses);
        Assert.Equal(0d, summary.TotalHours, 3);
    }

    [Fact]
    public void Semester_spanning_new_year_is_counted_continuously()
    {
        var start = new DateOnly(2026, 12, 7);  // понедельник
        var end = new DateOnly(2027, 1, 18);    // понедельник, 6 недель спустя

        var count = AcademicHoursCalculator.CountOccurrences(Entry(DayOfWeek.Monday), start, end);

        Assert.Equal(7, count);
    }
}
