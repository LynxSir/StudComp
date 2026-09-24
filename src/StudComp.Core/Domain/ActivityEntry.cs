namespace StudComp.Core.Domain;

/// <summary>
/// Вид события в ленте активности (ARCHITECTURE §7.1 <c>ACTIVITY_LOG.Kind</c>). Дашборд агрегирует
/// ленту, чтобы показать «последние файлы / отчёты / посещённые пары» (new_addons.md §1.5, §2).
/// </summary>
public enum ActivityKind
{
    /// <summary>Пользователь открыл файл из программы во внешнем приложении.</summary>
    FileOpened = 0,

    /// <summary>Архивариус разложил файл в папку предмета.</summary>
    FileSorted = 1,

    /// <summary>Сгенерирован отчёт.</summary>
    ReportGenerated = 2,

    /// <summary>Изменена заметка (пишется с Phase 12.3, когда появятся заметки).</summary>
    NoteEdited = 3,

    /// <summary>Отмечено посещение пары (пишется с Phase 12.3).</summary>
    ClassAttended = 4,

    /// <summary>Файл импортирован в папку предмета перетаскиванием (new_addons.md §1.11).</summary>
    FileImported = 5,

    /// <summary>Создана карточка знания (new_addons.md §3.6).</summary>
    CardCreated = 6,

    /// <summary>Карточка повторена в сессии (new_addons.md §3.6).</summary>
    CardReviewed = 7,
}

/// <summary>
/// Одна запись ленты активности (ARCHITECTURE §7.1 <c>ACTIVITY_LOG</c>). Источник данных для зон
/// Дашборда «последние файлы / отчёты». Ретеншн — ~500 записей / 90 дней, чистится фоном при старте.
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class ActivityEntry
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public ActivityKind Kind { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Предмет, к которому относится событие, либо <see langword="null"/>.</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>Путь к файлу/папке события, если применимо.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Ссылка на связанную сущность (запись файла, отчёт, дедлайн) — полиморфная, без FK:
    /// смысл зависит от <see cref="Kind"/>.
    /// </summary>
    public Guid? RefId { get; set; }

    /// <summary>Человекочитаемая подпись события — имя файла, название отчёта и т. п.</summary>
    public string? Title { get; set; }
}
