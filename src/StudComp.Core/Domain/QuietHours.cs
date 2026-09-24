namespace StudComp.Core.Domain;

/// <summary>
/// «Тихие часы» — окно, в которое напоминания не показываются (new_addons.md §11). Чистая функция:
/// значения окна приходят параметрами, потому что <c>Core</c> не знает про <c>NotificationOptions</c>
/// из <c>Infrastructure</c>, а потребителей два — Картотека и Органайзер.
/// </summary>
/// <remarks>
/// Окно может переходить через полночь (конец меньше начала — обычный случай 22:00–08:00). Начало
/// включительно, конец исключительно. Совпадение начала и конца трактуется как <b>пустое</b> окно:
/// вариант «тишина круглые сутки» молча выключил бы все напоминания и выглядел бы как поломка.
/// </remarks>
public static class QuietHours
{
    /// <summary>Попадает ли момент в тихие часы.</summary>
    public static bool IsQuiet(DateTimeOffset moment, TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            return false;
        }

        var now = TimeOnly.FromDateTime(moment.DateTime);

        return start < end
            ? now >= start && now < end
            : now >= start || now < end;
    }

    /// <summary>
    /// Ближайший момент, когда показывать уже можно. Если сейчас не тихо — сам момент; иначе конец
    /// окна. Напоминание именно <b>переносится</b>, а не отменяется: пользователь с окном 22:00–08:00
    /// и временем напоминания 23:00 иначе не получал бы его никогда и считал это поломкой.
    /// </summary>
    public static DateTimeOffset NextAllowedMoment(DateTimeOffset moment, TimeOnly start, TimeOnly end)
    {
        if (!IsQuiet(moment, start, end))
        {
            return moment;
        }

        var candidate = new DateTimeOffset(
            moment.Year,
            moment.Month,
            moment.Day,
            end.Hour,
            end.Minute,
            end.Second,
            moment.Offset);

        return candidate <= moment ? candidate.AddDays(1) : candidate;
    }
}
