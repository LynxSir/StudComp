namespace StudComp.Core.Domain;

/// <summary>Режим работы с карточками (new_addons.md §5.5).</summary>
public enum StudyMode
{
    /// <summary>Повторение: только карточки, у которых подошёл срок. Влияет на интервалы.</summary>
    Review = 0,

    /// <summary>Тренировка: любая выборка по фильтру, без ограничения по сроку.</summary>
    Practice = 1,

    /// <summary>Пробный экзамен: N случайных карточек, таймер, без подсказок. Измеряет, а не учит.</summary>
    Exam = 2,

    /// <summary>Работа над ошибками: карточки, на которых были промахи.</summary>
    Mistakes = 3,

    /// <summary>Аврал: обратный отсчёт к экзамену, материал гоняется циклами до даты.</summary>
    Cram = 4,
}

/// <summary>
/// Одна сессия работы с карточками (new_addons.md §3.1). Сохраняется <b>при старте</b>, а не по
/// завершении — по образцу <see cref="ReportJob"/>, который пишется до рендера: иначе прерванная
/// сессия не оставила бы следа и «продолжить» было бы нечего.
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class StudySession
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public StudyMode Mode { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Когда сессия закончена; <see langword="null"/> — прервана или ещё идёт.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public Guid? SubjectId { get; set; }

    public Guid? DeckId { get; set; }

    /// <summary>
    /// Снимок фильтра сессии в JSON — чтобы «пройти такую же ещё раз» не пришлось собирать заново.
    /// Хранится колонкой-значением, как <see cref="Semester.PairSlotsJson"/>.
    /// </summary>
    public string FilterJson { get; set; } = "{}";

    /// <summary>
    /// Зерно рандомизации. Порядок карточек — чистая функция от него, поэтому сессию можно честно
    /// тестировать и в точности повторить (new_addons.md §5.4).
    /// </summary>
    public int Seed { get; set; }

    public int PlannedCount { get; set; }

    public int AnsweredCount { get; set; }

    public int CorrectCount { get; set; }

    /// <summary>Лимит времени на сессию; <see langword="null"/> — без ограничения.</summary>
    public int? TimeLimitSeconds { get; set; }
}
