namespace StudComp.Core.Domain;

/// <summary>
/// Подсказка времени новой пары в тот же день: конец последней пары этого дня + перерыв, с учётом
/// обеденного окна (new_addons.md §5.1). Чистый калькулятор, только BCL — рядом с
/// <see cref="WeekParityCalculator"/>/<see cref="AcademicHoursCalculator"/> (ADR §16.21).
/// Ничего не знает про БД/семестр — вызывающий сам собирает вход из уже загруженных данных.
/// </summary>
public static class NextPairTimeCalculator
{
    /// <summary>Предложенное время новой пары.</summary>
    /// <param name="Start">Начало.</param>
    /// <param name="End">Конец (<paramref name="Start"/> + длительность пары).</param>
    /// <param name="BasedOnPreviousPair">
    /// <see langword="true"/> — время посчитано от конца существующей пары этого дня;
    /// <see langword="false"/> — в этот день пар ещё нет, использован запасной вариант.
    /// </param>
    public readonly record struct Suggestion(TimeOnly Start, TimeOnly End, bool BasedOnPreviousPair);

    /// <summary>
    /// Предложить время новой пары. Если в этот день уже есть пары — начало считается от конца самой
    /// поздней из них плюс <paramref name="breakDuration"/>, с поправкой на обеденное окно (если начало
    /// попало в него — сдвигается на его конец). Если пар ещё нет — используется <paramref name="fallbackStart"/>.
    /// </summary>
    public static Suggestion Suggest(
        IReadOnlyList<ScheduleEntry> sameDayEntries,
        TimeSpan pairDuration,
        TimeSpan breakDuration,
        (TimeOnly Start, TimeOnly End)? lunchBreak,
        TimeOnly fallbackStart)
    {
        ArgumentNullException.ThrowIfNull(sameDayEntries);

        if (sameDayEntries.Count == 0)
        {
            return new Suggestion(fallbackStart, fallbackStart.Add(pairDuration), false);
        }

        var start = sameDayEntries.Max(e => e.EndTime).Add(breakDuration);
        if (lunchBreak is { } lunch && start >= lunch.Start && start < lunch.End)
        {
            start = lunch.End;
        }

        return new Suggestion(start, start.Add(pairDuration), true);
    }

    /// <summary>Обеденное окно семестра как пара времён, либо <see langword="null"/>, если не задано.</summary>
    public static (TimeOnly Start, TimeOnly End)? LunchOf(Semester? semester) =>
        semester?.LunchBreakStart is { } start && semester.LunchBreakEnd is { } end
            ? (start, end)
            : null;
}
