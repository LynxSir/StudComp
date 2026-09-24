namespace StudComp.Core.Domain;

/// <summary>
/// Вид аттестации, который фиксирует <see cref="GradeEntry"/> (ARCHITECTURE §7.1 <c>GRADE_ENTRY.Type</c>).
/// </summary>
public enum GradeEntryType
{
    Exam = 0,
    Test = 1,
    CourseWork = 2,
    Attestation = 3,
}

/// <summary>
/// Одна оценка в зачётке по предмету (ARCHITECTURE §7.1 <c>GRADE_ENTRY</c>).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class GradeEntry
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    public GradeEntryType Type { get; set; }

    /// <summary>
    /// Человекочитаемое имя строки зачётки, например «Лабораторная 4». Для запланированных
    /// аттестаций попадает в <see cref="StudComp.Core.Abstractions.Organizer.PendingAssessment.Name"/>.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// <see langword="true"/> — аттестация запланирована, но ещё не оценена: прогноз считает её
    /// в <c>Pending</c> и экстраполирует на неё темп (ARCHITECTURE §9.4). <see cref="RawScore"/> при
    /// этом не используется.
    /// </summary>
    public bool IsPlanned { get; set; }

    /// <summary>Фактически набранный балл. Нормализуется по <see cref="MaxScore"/> в стратегии прогноза (ARCHITECTURE §9.4).</summary>
    public decimal RawScore { get; set; }

    /// <summary>Максимально возможный балл за эту аттестацию.</summary>
    public decimal MaxScore { get; set; }

    /// <summary>Вес компонента в итоговой оценке; нормализуется в <c>IGradeForecastStrategy</c> (ARCHITECTURE §7.2).</summary>
    public decimal Weight { get; set; }

    public DateTime Date { get; set; }

    public int Semester { get; set; }
}
