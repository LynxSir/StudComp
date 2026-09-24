namespace StudComp.Services;

/// <summary>
/// Единое форматирование статуса активного предмета — используется и чипом титул-бара
/// (<see cref="StudComp.ViewModels.Shell.MainWindowViewModel"/>), и геройской карточкой
/// «Активный предмет» на Дашборде (Phase 13.5), чтобы формулировки не разъезжались между двумя
/// местами интерфейса.
/// </summary>
public static class ActiveSubjectPresenter
{
    /// <summary>«Сейчас: X · до HH:mm» / «Далее: X через N мин/ч» / «X (закреплено)» / «Свободно».</summary>
    public static string FormatCaption(ActiveSubjectInfo? info)
    {
        if (info is null)
        {
            return "Свободно";
        }

        if (info.IsPinned)
        {
            return $"Сейчас: {info.SubjectName} (закреплено)";
        }

        if (info.IsOnNow)
        {
            var till = info.EndsIn is { } e ? $" · до {FormatClock(e)}" : string.Empty;
            return $"Сейчас: {info.SubjectName}{till}";
        }

        var starts = info.StartsIn is { } s ? $" через {FormatDelay(s)}" : string.Empty;
        return $"Далее: {info.SubjectName}{starts}";
    }

    private static string FormatClock(TimeSpan untilEnd)
    {
        var end = DateTime.Now + untilEnd;
        return end.ToString("HH:mm");
    }

    private static string FormatDelay(TimeSpan delay) => delay.TotalHours >= 1
        ? $"{(int)delay.TotalHours} ч {delay.Minutes} мин"
        : $"{Math.Max(1, (int)delay.TotalMinutes)} мин";
}
