namespace StudComp.Core.Domain;

/// <summary>Сколько раз пара проходит за семестр: всего, уже проведено и осталось.</summary>
public readonly record struct ClassOccurrences(int Total, int Held, int Remaining);

/// <summary>Часы по одному типу занятий за семестр (астрономические, по длительности пары).</summary>
public readonly record struct TypeHours(ScheduleEntryType Type, int Classes, double TotalHours, double HeldHours);

/// <summary>Сводка часов предмета за семестр — данные карточки «Обзор» в Хабе предмета.</summary>
public sealed record AcademicHoursSummary(
    IReadOnlyList<TypeHours> ByType,
    int TotalClasses,
    int HeldClasses,
    int RemainingClasses,
    double TotalHours,
    double HeldHours,
    double RemainingHours,
    DateOnly? NextClassDate);

/// <summary>
/// Ориентировочные часы за семестр (new_addons.md §5): сколько раз каждая пара пройдёт между датами
/// семестра с учётом чётности недели, и сколько из этого уже позади.
/// </summary>
/// <remarks>
/// Чистая календарная логика, только BCL — живёт в <c>Core</c> рядом с
/// <see cref="WeekParityCalculator"/> (ADR §16.21). Часы считаются <b>астрономические</b>, по
/// фактической длительности пары: пересчёт в «академические» по 45 минут — правило конкретного вуза,
/// в ARCHITECTURE его нет, и выдумывать его здесь нельзя.
/// </remarks>
public static class AcademicHoursCalculator
{
    /// <summary>
    /// Число проведений пары в интервале <paramref name="semesterStart"/>..<paramref name="semesterEnd"/>
    /// включительно. Ноль, если интервал пуст или ни одна дата не подходит по чётности.
    /// </summary>
    public static int CountOccurrences(
        ScheduleEntry entry,
        DateOnly semesterStart,
        DateOnly semesterEnd,
        bool firstWeekIsOdd = true)
    {
        var count = 0;
        foreach (var _ in EnumerateOccurrences(entry, semesterStart, semesterEnd, firstWeekIsOdd))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// То же с разбивкой «проведено / осталось» на дату <paramref name="asOf"/>. Пара, приходящаяся
    /// ровно на <paramref name="asOf"/>, считается предстоящей: календарный расчёт не знает времени
    /// суток, и тянуть сюда понятие «сейчас» было бы неверно.
    /// </summary>
    public static ClassOccurrences CountOccurrences(
        ScheduleEntry entry,
        DateOnly semesterStart,
        DateOnly semesterEnd,
        DateOnly asOf,
        bool firstWeekIsOdd = true)
    {
        var total = 0;
        var held = 0;

        foreach (var date in EnumerateOccurrences(entry, semesterStart, semesterEnd, firstWeekIsOdd))
        {
            total++;
            if (date < asOf)
            {
                held++;
            }
        }

        return new ClassOccurrences(total, held, total - held);
    }

    /// <summary>
    /// Ближайшее проведение пары начиная с <paramref name="asOf"/> включительно, либо
    /// <see langword="null"/>, если до конца семестра таких нет.
    /// </summary>
    public static DateOnly? NextOccurrence(
        ScheduleEntry entry,
        DateOnly semesterStart,
        DateOnly semesterEnd,
        DateOnly asOf,
        bool firstWeekIsOdd = true)
    {
        foreach (var date in EnumerateOccurrences(entry, semesterStart, semesterEnd, firstWeekIsOdd))
        {
            if (date >= asOf)
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>Сводка по всем парам предмета: разбивка по типам занятий и итог.</summary>
    public static AcademicHoursSummary Summarize(
        IReadOnlyList<ScheduleEntry> entries,
        DateOnly semesterStart,
        DateOnly semesterEnd,
        DateOnly asOf,
        bool firstWeekIsOdd = true)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var byType = new Dictionary<ScheduleEntryType, (int Classes, double Total, double Held)>();
        var totalClasses = 0;
        var heldClasses = 0;
        var totalHours = 0d;
        var heldHours = 0d;
        DateOnly? nextDate = null;

        foreach (var entry in entries)
        {
            var occurrences = CountOccurrences(entry, semesterStart, semesterEnd, asOf, firstWeekIsOdd);
            if (occurrences.Total == 0)
            {
                continue;
            }

            var lessonHours = LessonHours(entry);
            var entryTotalHours = occurrences.Total * lessonHours;
            var entryHeldHours = occurrences.Held * lessonHours;

            var current = byType.GetValueOrDefault(entry.Type);
            byType[entry.Type] = (
                current.Classes + occurrences.Total,
                current.Total + entryTotalHours,
                current.Held + entryHeldHours);

            totalClasses += occurrences.Total;
            heldClasses += occurrences.Held;
            totalHours += entryTotalHours;
            heldHours += entryHeldHours;

            var next = NextOccurrence(entry, semesterStart, semesterEnd, asOf, firstWeekIsOdd);
            if (next is { } candidate && (nextDate is null || candidate < nextDate))
            {
                nextDate = candidate;
            }
        }

        var list = byType
            .OrderBy(pair => pair.Key)
            .Select(pair => new TypeHours(pair.Key, pair.Value.Classes, pair.Value.Total, pair.Value.Held))
            .ToArray();

        return new AcademicHoursSummary(
            list,
            totalClasses,
            heldClasses,
            totalClasses - heldClasses,
            totalHours,
            heldHours,
            totalHours - heldHours,
            nextDate);
    }

    /// <summary>
    /// Даты всех проведений пары по возрастанию. Идём неделями от первого подходящего дня, а не
    /// считаем замкнутой формулой: кандидатов десятки, цикл очевидно верен, а формула с делением
    /// пополам — источник ошибки на единицу.
    /// </summary>
    private static IEnumerable<DateOnly> EnumerateOccurrences(
        ScheduleEntry entry,
        DateOnly semesterStart,
        DateOnly semesterEnd,
        bool firstWeekIsOdd)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (semesterEnd < semesterStart)
        {
            yield break;
        }

        var offset = ((int)entry.DayOfWeek - (int)semesterStart.DayOfWeek + 7) % 7;
        var date = semesterStart.AddDays(offset);

        while (date <= semesterEnd)
        {
            if (entry.WeekParity == WeekParity.Any
                || WeekParityCalculator.GetParity(semesterStart, date, firstWeekIsOdd) == entry.WeekParity)
            {
                yield return date;
            }

            date = date.AddDays(7);
        }
    }

    /// <summary>Длительность одной пары в часах; некорректный интервал даёт ноль, а не исключение.</summary>
    private static double LessonHours(ScheduleEntry entry) =>
        entry.EndTime > entry.StartTime ? (entry.EndTime - entry.StartTime).TotalHours : 0d;
}
