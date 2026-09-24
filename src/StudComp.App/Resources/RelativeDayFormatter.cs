using System.Globalization;

namespace StudComp.Resources;

/// <summary>
/// Относительное форматирование даты дедлайна для Дашборда (Phase 13.5) — «Сегодня, 18:00» /
/// «Завтра, 09:00» / «через 3 дня» / «просрочено 2 дня назад» — вместо голого «dd.MM HH:mm».
/// <see cref="CultureInfo.InvariantCulture"/> — по тому же принципу, что и <see cref="AppDateFormat"/>:
/// не зависеть от локали ОС.
/// </summary>
public static class RelativeDayFormatter
{
    public static string Format(DateTimeOffset dueDate)
    {
        var now = DateTimeOffset.Now;
        var dueLocal = dueDate.ToLocalTime();
        var nowDay = DateOnly.FromDateTime(now.LocalDateTime);
        var dueDay = DateOnly.FromDateTime(dueLocal.LocalDateTime);
        var days = dueDay.DayNumber - nowDay.DayNumber;
        var time = dueLocal.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (dueDate < now)
        {
            var overdueDays = Math.Abs(days);
            return overdueDays == 0
                ? $"Просрочено сегодня, {time}"
                : $"Просрочено {overdueDays} {RussianPlural.Of(overdueDays, "день", "дня", "дней")} назад";
        }

        return days switch
        {
            0 => $"Сегодня, {time}",
            1 => $"Завтра, {time}",
            _ when days <= 6 => $"через {days} {RussianPlural.Of(days, "день", "дня", "дней")}",
            _ => dueLocal.ToString(AppDateFormat.ShortDate, CultureInfo.InvariantCulture),
        };
    }
}
