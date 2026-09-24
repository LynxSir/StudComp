using System.Globalization;
using System.Windows.Media;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Organizer;

/// <summary>
/// Карточка предмета в гриде «Предметы» (new_addons.md §5): цвет, ближайшая пара, часы за семестр,
/// число открытых дедлайнов и чип прогноза. Клик по карточке ведёт в Хаб предмета.
/// </summary>
public sealed class SubjectCardViewModel
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public SubjectCardViewModel(
        Subject subject,
        ScheduleEntry? nextClass,
        DateOnly? nextClassDate,
        double? totalHours,
        int openDeadlines,
        int overdueDeadlines,
        decimal? predictedScore)
    {
        Subject = subject;
        AccentBrush = SubjectColor.BrushFor(subject.ColorHex);
        FillBrush = SubjectColor.TintFor(subject.ColorHex);

        OpenDeadlines = openDeadlines;
        OverdueDeadlines = overdueDeadlines;

        NextClassText = nextClass is null
            ? "Пар в расписании нет"
            : BuildNextClassText(nextClass, nextClassDate);

        HoursText = totalHours is { } hours
            ? $"≈ {(Math.Round(hours * 2, MidpointRounding.AwayFromZero) / 2).ToString("0.#", Ru)} ч за семестр"
            : string.Empty;

        ForecastText = predictedScore is { } score ? score.ToString("0.#", Ru) : string.Empty;
    }

    public Subject Subject { get; }

    public Guid Id => Subject.Id;

    public string Name => Subject.Name;

    public string Code => Subject.Code;

    public string AssessmentText => Subject.Assessment.DisplayName();

    public Brush AccentBrush { get; }

    public Brush FillBrush { get; }

    public string NextClassText { get; }

    public string HoursText { get; }

    public bool HasHours => HoursText.Length > 0;

    public int OpenDeadlines { get; }

    public int OverdueDeadlines { get; }

    public bool HasDeadlines => OpenDeadlines > 0;

    public bool HasOverdue => OverdueDeadlines > 0;

    public string DeadlinesText => OverdueDeadlines > 0
        ? $"{OpenDeadlines} дедл. · {OverdueDeadlines} просроч."
        : $"{OpenDeadlines} дедл.";

    /// <summary>Прогноз итоговой оценки; пусто — оценок ещё нет.</summary>
    public string ForecastText { get; }

    public bool HasForecast => ForecastText.Length > 0;

    private static string BuildNextClassText(ScheduleEntry entry, DateOnly? date)
    {
        var time = entry.StartTime.ToString("HH\\:mm", CultureInfo.InvariantCulture);
        var type = OrganizerChoices.ScheduleTypeName(entry.Type);

        if (date is not { } day)
        {
            return $"{OrganizerChoices.DayName(entry.DayOfWeek)}, {time} · {type}";
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var prefix = day == today
            ? "Сегодня"
            : day == today.AddDays(1) ? "Завтра" : day.ToString("dd MMMM", Ru);

        return $"{prefix}, {time} · {type}";
    }
}
