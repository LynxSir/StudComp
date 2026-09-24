namespace StudComp.Core.Domain;

/// <summary>
/// Вид заметки (new_addons.md §1.9): чем она была по замыслу автора — конспектом пары, пометкой к
/// файлу или папке, либо свободной записью.
/// </summary>
public enum NoteKind
{
    /// <summary>Конспект лекции/пары.</summary>
    Lecture = 0,

    /// <summary>Пометка к конкретному файлу.</summary>
    FileNote = 1,

    /// <summary>Пометка к папке.</summary>
    FolderNote = 2,

    /// <summary>Свободная заметка, ни к чему не привязанная.</summary>
    Free = 3,

    /// <summary>Текст задания дедлайна — служебная заметка, в общих списках не показывается.</summary>
    DeadlineTask = 4,

    /// <summary>Текст ответа на дедлайн — служебная заметка, в общих списках не показывается.</summary>
    DeadlineAnswer = 5,
}

/// <summary>
/// Заметка в Markdown (new_addons.md §1.9). Может быть привязана к предмету, файлу, папке и паре —
/// все привязки необязательные и при исчезновении цели обнуляются, сама заметка живёт дальше: это
/// пользовательский текст, терять его нельзя (§14 по духу).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class Note
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Предмет заметки; <see langword="null"/> — заметка вне предмета.</summary>
    public Guid? SubjectId { get; set; }

    public NoteKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Тело заметки в Markdown. Пишется автосохранением, кнопки «Сохранить» нет.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>Путь файла или папки внутри учебной папки, к которым относится заметка.</summary>
    public string? LinkedPath { get; set; }

    /// <summary>Запись учёта файла, если заметка сделана к разложенному архивариусом файлу.</summary>
    public Guid? LinkedFileRecordId { get; set; }

    /// <summary>Пара, с которой сделана заметка (вместе с <see cref="ClassDate"/>).</summary>
    public Guid? ScheduleEntryId { get; set; }

    /// <summary>
    /// Дедлайн, которому принадлежит служебная заметка (<see cref="NoteKind.DeadlineTask"/> /
    /// <see cref="NoteKind.DeadlineAnswer"/>); у обычных заметок <see langword="null"/>. Единственная
    /// привязка с каскадом: такая заметка — часть дедлайна, а не самостоятельный текст.
    /// </summary>
    public Guid? DeadlineId { get; set; }

    /// <summary>Дата конкретного проведения пары — «заметка с пары 12.09».</summary>
    public DateOnly? ClassDate { get; set; }

    /// <summary>Закреплённые заметки идут в списке первыми.</summary>
    public bool IsPinned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Время последней правки — по нему сортируются «последние заметки» на Дашборде.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
