using System.Globalization;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Organizer;

/// <summary>Строка списка семестров в настройках: готовые подписи, без логики.</summary>
public sealed class SemesterRowViewModel
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public SemesterRowViewModel(Semester semester, int subjectCount)
    {
        Semester = semester;
        SubjectCount = subjectCount;

        var start = semester.StartDate.ToString("dd.MM.yyyy", Ru);
        var end = semester.EndDate is { } e ? e.ToString("dd.MM.yyyy", Ru) : "не задана";
        DatesText = $"{start} — {end}";

        var slots = PairSlots.Parse(semester.PairSlotsJson).Count;
        DetailsText = $"{semester.CourseNumber} курс · первая неделя — "
            + (semester.FirstWeekIsOdd ? "числитель" : "знаменатель")
            + $" · пар в сетке: {slots} · предметов: {subjectCount}";
    }

    public Semester Semester { get; }

    public Guid Id => Semester.Id;

    public string Name => Semester.Name;

    public bool IsActive => Semester.IsActive;

    public int SubjectCount { get; }

    public string DatesText { get; }

    public string DetailsText { get; }

    /// <summary>Подсветить, что часы за семестр посчитать нельзя, пока не задана дата окончания.</summary>
    public bool NeedsEndDate => Semester.EndDate is null;
}
