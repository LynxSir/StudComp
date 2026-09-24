namespace StudComp.Core.Domain;

/// <summary>
/// Тип занятия в расписании (ARCHITECTURE §7.1 <c>SCHEDULE_ENTRY.Type</c>).
/// </summary>
public enum ScheduleEntryType
{
    Lecture = 0,
    Seminar = 1,
    Lab = 2,

    /// <summary>Практическое занятие.</summary>
    Practice = 3,

    /// <summary>Консультация.</summary>
    Consultation = 4,

    /// <summary>Экзамен или зачёт по расписанию сессии.</summary>
    Exam = 5,
}

/// <summary>
/// Чётность недели, на которой проходит занятие — для вузов с чередованием недель
/// (ARCHITECTURE §7.1 <c>SCHEDULE_ENTRY.WeekParity</c>, §9.2). В привычных терминах —
/// числитель и знаменатель: они сменяют друг друга строго по очереди от даты начала семестра,
/// двух одинаковых недель подряд не бывает (<see cref="WeekParityCalculator"/>).
/// </summary>
public enum WeekParity
{
    /// <summary>Каждую неделю, независимо от числителя/знаменателя.</summary>
    Any = 0,

    /// <summary>Только по нечётным неделям — «числитель».</summary>
    Odd = 1,

    /// <summary>Только по чётным неделям — «знаменатель».</summary>
    Even = 2,
}

/// <summary>
/// Одна пара в недельной сетке расписания (ARCHITECTURE §7.1 <c>SCHEDULE_ENTRY</c>, §9.2).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class ScheduleEntry
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    /// <summary>День недели, в который проходит занятие.</summary>
    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>Аудитория.</summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>Преподаватель.</summary>
    public string Teacher { get; set; } = string.Empty;

    public ScheduleEntryType Type { get; set; }

    public WeekParity WeekParity { get; set; }
}
