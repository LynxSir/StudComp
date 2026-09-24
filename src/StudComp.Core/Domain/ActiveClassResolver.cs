namespace StudComp.Core.Domain;

/// <summary>
/// Пара, которая идёт сейчас или начнётся ближайшей (new_addons.md §1.8). Чистый результат расчёта —
/// пиксели/подписи собирает вызывающий (App-слой).
/// </summary>
public readonly record struct ActiveClass(ScheduleEntry Entry, bool IsOnNow, TimeSpan StartsIn, TimeSpan EndsIn);

/// <summary>
/// Выбор «пары, которая идёт сейчас / ближайшей следующей» по недельной сетке расписания с учётом
/// чётности недели (new_addons.md §1.8). Чистая календарная логика, только BCL — живёт в <c>Core</c>
/// рядом с <see cref="WeekParityCalculator"/> (ADR §16.21).
/// </summary>
public static class ActiveClassResolver
{
    private const int LookAheadDays = 7;

    /// <summary>
    /// Возвращает идущую сейчас пару (<see cref="ActiveClass.IsOnNow"/> = <see langword="true"/>) либо
    /// ближайшую следующую в пределах недели, либо <see langword="null"/>, если подходящих нет.
    /// </summary>
    /// <param name="entries">Все записи расписания (по всем дням недели).</param>
    /// <param name="now">Текущий момент (локальное настенное время в его <see cref="DateTimeOffset.DateTime"/>).</param>
    /// <param name="semesterStart">
    /// Дата начала семестра для расчёта чётности. <see langword="null"/> — фильтр по чётности не
    /// применяется (семестр ещё не настроен).
    /// </param>
    /// <param name="firstWeekIsOdd">Считать ли первую неделю семестра нечётной.</param>
    public static ActiveClass? Resolve(
        IReadOnlyList<ScheduleEntry> entries,
        DateTimeOffset now,
        DateOnly? semesterStart,
        bool firstWeekIsOdd = true)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return null;
        }

        var reference = now.DateTime;
        var today = DateOnly.FromDateTime(reference);
        var nowTime = TimeOnly.FromDateTime(reference);

        // Идущая прямо сейчас пара — приоритет над любой будущей.
        ActiveClass? runningNow = null;
        ActiveClass? nextToday = null;

        foreach (var entry in entries)
        {
            if (entry.DayOfWeek != today.DayOfWeek || !AppliesOn(entry, today, semesterStart, firstWeekIsOdd))
            {
                continue;
            }

            if (entry.StartTime <= nowTime && nowTime < entry.EndTime)
            {
                var candidate = new ActiveClass(entry, IsOnNow: true, TimeSpan.Zero, entry.EndTime - nowTime);
                if (runningNow is null || entry.EndTime < runningNow.Value.Entry.EndTime)
                {
                    runningNow = candidate;
                }
            }
            else if (entry.StartTime > nowTime)
            {
                var startsIn = entry.StartTime - nowTime;
                if (nextToday is null || startsIn < nextToday.Value.StartsIn)
                {
                    nextToday = new ActiveClass(entry, IsOnNow: false, startsIn, entry.EndTime - nowTime);
                }
            }
        }

        if (runningNow is not null)
        {
            return runningNow;
        }

        if (nextToday is not null)
        {
            return nextToday;
        }

        // Ничего сегодня — ищем ближайшую пару в следующие дни недели.
        for (var offset = 1; offset <= LookAheadDays; offset++)
        {
            var day = today.AddDays(offset);
            ActiveClass? best = null;

            foreach (var entry in entries)
            {
                if (entry.DayOfWeek != day.DayOfWeek || !AppliesOn(entry, day, semesterStart, firstWeekIsOdd))
                {
                    continue;
                }

                var startsAt = day.ToDateTime(entry.StartTime);
                var startsIn = startsAt - reference;
                if (best is null || startsIn < best.Value.StartsIn)
                {
                    best = new ActiveClass(entry, IsOnNow: false, startsIn, day.ToDateTime(entry.EndTime) - reference);
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private static bool AppliesOn(ScheduleEntry entry, DateOnly date, DateOnly? semesterStart, bool firstWeekIsOdd)
    {
        if (entry.WeekParity == WeekParity.Any || semesterStart is not { } start)
        {
            return true;
        }

        return WeekParityCalculator.GetParity(start, date, firstWeekIsOdd) == entry.WeekParity;
    }
}
