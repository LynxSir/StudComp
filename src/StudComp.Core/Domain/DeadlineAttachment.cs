namespace StudComp.Core.Domain;

/// <summary>К чему относится вложение дедлайна.</summary>
public enum DeadlineAttachmentRole
{
    /// <summary>Материалы задания: методичка, условие, исходные данные.</summary>
    Task = 0,

    /// <summary>Файлы ответа: готовая работа, отчёт, архив с кодом.</summary>
    Answer = 1,
}

/// <summary>
/// Файл, приложенный к дедлайну. Сам файл лежит в папке дедлайна внутри папки предмета
/// (<c>Дедлайны/&lt;папка&gt;/Материалы</c> или <c>…/Ответ</c>); здесь — только учёт.
/// Удаление записи файл не трогает (ARCHITECTURE §14).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class DeadlineAttachment
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public Guid DeadlineId { get; set; }

    public DeadlineAttachmentRole Role { get; set; }

    /// <summary>Имя файла для показа в списке.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Путь относительно учебной папки — переживает перенос учебной папки на другой диск.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; }
}
