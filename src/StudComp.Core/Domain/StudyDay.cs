namespace StudComp.Core.Domain;

/// <summary>
/// Граница «учебных суток». Студент, отвечающий на карточки в час ночи, считает это вчерашним
/// вечером — и дневные лимиты, серия дней и календарь активности обязаны считать так же, иначе
/// полуночная сессия обнуляет счётчики посреди работы.
/// </summary>
public static class StudyDay
{
    /// <summary>Начало учебных суток, которым принадлежит момент.</summary>
    public static DateTimeOffset StartOf(DateTimeOffset moment, int rolloverHour)
    {
        var hour = Math.Clamp(rolloverHour, 0, 23);
        var start = new DateTimeOffset(moment.Year, moment.Month, moment.Day, hour, 0, 0, moment.Offset);
        return moment < start ? start.AddDays(-1) : start;
    }

    /// <summary>Календарный день, которым датируется момент.</summary>
    public static DateOnly DayOf(DateTimeOffset moment, int rolloverHour) =>
        DateOnly.FromDateTime(StartOf(moment, rolloverHour).Date);

    /// <summary>
    /// Сдвиг в минутах, приводящий хранимое UTC-время к «учебным суткам»: местное смещение минус час
    /// начала суток. Нужен агрегатам «по дням» в слое данных — там даты лежат в UTC, и без сдвига
    /// календарь активности уезжает на часовой пояс.
    /// </summary>
    public static int OffsetMinutes(TimeSpan localOffset, int rolloverHour) =>
        (int)localOffset.TotalMinutes - (Math.Clamp(rolloverHour, 0, 23) * 60);
}
