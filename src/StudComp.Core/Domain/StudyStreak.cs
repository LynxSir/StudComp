namespace StudComp.Core.Domain;

/// <summary>Серия дней подряд с занятиями (new_addons.md §6.5) — одна строка, без геймификации.</summary>
/// <param name="Current">Текущая серия; ноль — серии нет.</param>
/// <param name="Longest">Самая длинная серия за всё время.</param>
/// <param name="LastActiveDay">Последний день с ответами.</param>
public sealed record StudyStreakInfo(int Current, int Longest, DateOnly? LastActiveDay)
{
    /// <summary>Пустая история.</summary>
    public static StudyStreakInfo Empty { get; } = new(0, 0, null);
}

/// <summary>Считает серию дней подряд. Чистая функция на голом BCL.</summary>
public static class StudyStreak
{
    /// <summary>
    /// Посчитать серию.
    /// </summary>
    /// <remarks>
    /// Текущая серия считается живой, если последний активный день — сегодня <b>или вчера</b>: иначе
    /// строка «серия 12 дней» обнулялась бы каждое утро, пока пользователь не сядет заниматься, и
    /// показывала бы ноль ровно в тот момент, когда должна мотивировать.
    /// </remarks>
    public static StudyStreakInfo Calculate(IReadOnlyCollection<DateOnly> activeDays, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(activeDays);

        if (activeDays.Count == 0)
        {
            return StudyStreakInfo.Empty;
        }

        var days = activeDays.Distinct().OrderBy(d => d).ToList();

        var longest = 1;
        var run = 1;
        for (var i = 1; i < days.Count; i++)
        {
            run = days[i].DayNumber - days[i - 1].DayNumber == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        var last = days[^1];
        var gap = today.DayNumber - last.DayNumber;

        // Дата из будущего (сбитые часы, импорт) серию не ломает — она просто ещё не наступила.
        var current = gap <= 1 ? run : 0;

        return new StudyStreakInfo(current, longest, last);
    }
}
