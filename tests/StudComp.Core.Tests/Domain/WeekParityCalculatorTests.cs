using System.Globalization;
using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты <see cref="WeekParityCalculator"/> (PLAN.md Phase 4, §12). Дата
/// <c>2025-09-01</c> — понедельник, удобная точка отсчёта семестра.
/// </summary>
public sealed class WeekParityCalculatorTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2025-09-01", "2025-09-01", 1)] // первый день семестра
    [InlineData("2025-09-01", "2025-09-07", 1)] // воскресенье той же недели
    [InlineData("2025-09-01", "2025-09-08", 2)] // следующий понедельник
    [InlineData("2025-09-01", "2025-09-15", 3)]
    [InlineData("2025-09-01", "2025-12-15", 16)]
    [InlineData("2025-09-01", "2025-08-25", 0)]  // неделя до старта
    [InlineData("2025-09-01", "2025-08-18", -1)] // две недели до старта
    [InlineData("2025-09-03", "2025-09-01", 1)]  // старт в среду — обе даты в одной календарной неделе
    [InlineData("2025-09-03", "2025-09-08", 2)]  // старт в среду — дата на следующей неделе
    public void GetWeekNumber_counts_weeks_from_semester_start(string start, string date, int expected)
    {
        Assert.Equal(expected, WeekParityCalculator.GetWeekNumber(D(start), D(date)));
    }

    [Theory]
    [InlineData("2025-09-01", "2025-09-01", true, WeekParity.Odd)]   // неделя 1 — нечётная
    [InlineData("2025-09-01", "2025-09-07", true, WeekParity.Odd)]
    [InlineData("2025-09-01", "2025-09-08", true, WeekParity.Even)]  // неделя 2 — чётная
    [InlineData("2025-09-01", "2025-09-15", true, WeekParity.Odd)]   // неделя 3 — нечётная
    [InlineData("2025-09-01", "2025-09-01", false, WeekParity.Even)] // первая неделя чётная — инверсия
    [InlineData("2025-09-01", "2025-09-08", false, WeekParity.Odd)]
    [InlineData("2025-09-01", "2025-08-25", true, WeekParity.Even)]  // неделя 0 — противоположна первой
    public void GetParity_alternates_and_respects_firstWeekIsOdd(
        string start, string date, bool firstWeekIsOdd, WeekParity expected)
    {
        Assert.Equal(expected, WeekParityCalculator.GetParity(D(start), D(date), firstWeekIsOdd));
    }

    [Fact]
    public void GetParity_never_returns_Any()
    {
        var start = D("2025-09-01");
        for (var i = -5; i < 40; i++)
        {
            var parity = WeekParityCalculator.GetParity(start, start.AddDays(i * 3));
            Assert.True(parity is WeekParity.Odd or WeekParity.Even);
        }
    }

    /// <summary>
    /// Числитель и знаменатель сменяют друг друга строго по очереди: двух одинаковых недель подряд
    /// не бывает никогда. Вся чётность выводится из одной даты начала семестра и флага «первая
    /// неделя — числитель», отдельного календаря исключений нет.
    /// </summary>
    [Theory]
    [InlineData("2025-09-01", true)]   // старт в понедельник
    [InlineData("2025-09-03", true)]   // старт в середине недели
    [InlineData("2025-09-07", false)]  // старт в воскресенье, первая неделя — знаменатель
    [InlineData("2026-02-09", false)]
    public void Parity_strictly_alternates_week_after_week(string start, bool firstWeekIsOdd)
    {
        var semesterStart = D(start);
        var previous = WeekParityCalculator.GetParity(semesterStart, semesterStart, firstWeekIsOdd);

        // Полгода вперёд — заведомо длиннее любого семестра.
        for (var week = 1; week < 26; week++)
        {
            var current = WeekParityCalculator.GetParity(
                semesterStart, semesterStart.AddDays(week * 7), firstWeekIsOdd);

            Assert.NotEqual(previous, current);
            previous = current;
        }
    }

    /// <summary>Внутри одной календарной недели чётность одинакова во все семь дней.</summary>
    [Theory]
    [InlineData("2025-09-01", true)]
    [InlineData("2025-09-03", true)]
    [InlineData("2025-09-03", false)]
    public void Parity_is_the_same_for_every_day_of_one_week(string start, bool firstWeekIsOdd)
    {
        var semesterStart = D(start);

        // Неделя считается от понедельника, а семестр может начаться в любой день — поэтому
        // сдвигаемся к понедельнику той недели, в которую попадает старт.
        var firstMonday = semesterStart.AddDays(-(((int)semesterStart.DayOfWeek + 6) % 7));

        for (var week = 0; week < 8; week++)
        {
            var monday = firstMonday.AddDays(week * 7);
            var expected = WeekParityCalculator.GetParity(semesterStart, monday, firstWeekIsOdd);

            for (var day = 1; day < 7; day++)
            {
                Assert.Equal(
                    expected,
                    WeekParityCalculator.GetParity(semesterStart, monday.AddDays(day), firstWeekIsOdd));
            }
        }
    }
}
