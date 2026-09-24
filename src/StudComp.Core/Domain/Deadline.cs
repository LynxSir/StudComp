namespace StudComp.Core.Domain;

/// <summary>
/// Вид работы, у которой есть срок сдачи (ARCHITECTURE §7.1 <c>DEADLINE.Type</c>).
/// Набор значений выбран по месту — в ARCHITECTURE точного списка нет; сверить при UI (Phase 4).
/// </summary>
public enum DeadlineType
{
    Homework = 0,
    Test = 1,
    Exam = 2,
    CourseWork = 3,
    Other = 4,
}

/// <summary>
/// Приоритет дедлайна для сортировки и подсветки в интерфейсе
/// (ARCHITECTURE §7.1 <c>DEADLINE.Priority</c> — в ER это <c>int</c>, здесь типизировано).
/// </summary>
public enum DeadlinePriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>
/// Состояние дедлайна. <c>Overdue</c> сознательно отсутствует — оно производное
/// (<c>DueDate &lt; now &amp;&amp; Status == Pending</c>) и вычисляется в запросе/VM, а не хранится,
/// чтобы не рассинхронизироваться с текущим временем (ARCHITECTURE §9.3).
/// </summary>
public enum DeadlineStatus
{
    Pending = 0,
    Done = 1,
}

/// <summary>
/// Дедлайн по предмету: сдача, экзамен, контрольная точка (ARCHITECTURE §7.1 <c>DEADLINE</c>, §9.3).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class Deadline
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Момент, к которому нужно сдать работу.</summary>
    public DateTimeOffset DueDate { get; set; }

    public DeadlineType Type { get; set; }

    public DeadlinePriority Priority { get; set; }

    public DeadlineStatus Status { get; set; }

    /// <summary>
    /// Привязанный архивариусом файл (<see cref="FileRecord"/>), либо <see langword="null"/>.
    /// Связь предлагается через <c>FileSortedMessage</c>, а не прямым вызовом модулей (ARCHITECTURE §9.3).
    /// </summary>
    public Guid? LinkedFileRecordId { get; set; }

    /// <summary>
    /// Имя папки дедлайна внутри <c>Дедлайны/</c> папки предмета — туда складываются материалы,
    /// файлы ответа, картинки и <c>.md</c>-копии текстов. Задаётся один раз при первом появлении
    /// содержимого и вслед за заголовком не переименовывается: иначе файлы потерялись бы.
    /// <see langword="null"/> — папка ещё не создавалась.
    /// </summary>
    public string? FolderName { get; set; }

    /// <summary>Когда работа была отмечена сданной; <see langword="null"/> — ещё не сдана.</summary>
    public DateTimeOffset? AnsweredAt { get; set; }
}
