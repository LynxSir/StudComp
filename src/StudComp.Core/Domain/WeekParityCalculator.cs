namespace StudComp.Core.Domain;

/// <summary>
/// Расчёт номера и чётности учебной недели от даты начала семестра (ARCHITECTURE §9.2).
/// Чистая календарная логика, только BCL — живёт в <c>Core</c>, а не в модуле Органайзера
/// (PLAN.md §12 называет калькулятор примером доменной логики для unit-тестов).
/// </summary>
public static class WeekParityCalculator
{
    /// <summary>
    /// Номер учебной недели (начиная с 1) для <paramref name="date"/> относительно
    /// <paramref name="semesterStart"/>. Обе даты приводятся к понедельнику своей календарной недели,
    /// поэтому дни внутри одной недели дают один номер. Для дат раньше начала семестра номер ≤ 0.
    /// </summary>
    public static int GetWeekNumber(DateOnly semesterStart, DateOnly date)
    {
        var startMonday = ToMonday(semesterStart);
        var dateMonday = ToMonday(date);
        var weeksBetween = (dateMonday.DayNumber - startMonday.DayNumber) / 7;
        return weeksBetween + 1;
    }

    /// <summary>
    /// Чётность недели, на которую приходится <paramref name="date"/>. Возвращает только
    /// <see cref="WeekParity.Odd"/> или <see cref="WeekParity.Even"/> — <see cref="WeekParity.Any"/>
    /// здесь смысла не имеет.
    /// </summary>
    /// <param name="firstWeekIsOdd">
    /// Считать ли неделю, содержащую <paramref name="semesterStart"/>, нечётной. По умолчанию — да
    /// (первая неделя семестра обычно «первая = нечётная»); часть вузов начинает с чётной.
    /// </param>
    public static WeekParity GetParity(DateOnly semesterStart, DateOnly date, bool firstWeekIsOdd = true)
    {
        var weekNumber = GetWeekNumber(semesterStart, date);
        // Приводим к неотрицательному индексу, чтобы % не давал отрицательный остаток на датах до старта.
        var zeroBased = weekNumber - 1;
        var normalized = ((zeroBased % 2) + 2) % 2;
        var isOddWeek = firstWeekIsOdd ? normalized == 0 : normalized == 1;
        return isOddWeek ? WeekParity.Odd : WeekParity.Even;
    }

    private static DateOnly ToMonday(DateOnly date)
    {
        // DayOfWeek: воскресенье = 0; смещение до понедельника той же недели.
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}
