namespace StudComp.Core.Domain;

/// <summary>
/// Состояние файла в учёте архивариуса (ARCHITECTURE §7.1 <c>FILE_RECORD.Status</c>).
/// </summary>
public enum FileRecordStatus
{
    /// <summary>Файл обнаружен, но ещё не разобран — попадает в раздел «Неразобранное» (ARCHITECTURE §8.4 п.7).</summary>
    Detected = 0,

    /// <summary>Файл переименован и перемещён в папку предмета.</summary>
    Sorted = 1,

    /// <summary>Файл отложен: залочен другим процессом либо не прошёл проверки (ARCHITECTURE §8.6).</summary>
    Quarantined = 2,

    /// <summary>Сортировку отменил пользователь через Undo (ARCHITECTURE §8.6).</summary>
    UndoneByUser = 3,

    /// <summary>
    /// Файл был залочен в момент попытки; стоит в отложенной очереди с ретраем по бэкоффу
    /// (ARCHITECTURE §8.6, Phase 8). В отличие от <see cref="Quarantined"/>, наблюдатель
    /// повторно берёт такой файл в обработку. После исчерпания попыток переходит в <see cref="Quarantined"/>.
    /// </summary>
    Deferred = 4,
}

/// <summary>
/// Запись учёта об одном файле, который видел архивариус (ARCHITECTURE §7.1 <c>FILE_RECORD</c>, §8.2).
/// Пишется до фактической файловой операции ради crash-safety (ARCHITECTURE §8.4 п.6).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class FileRecord
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Предмет, к которому отнесён файл, либо <see langword="null"/> до сортировки.</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>Путь, по которому файл был обнаружен.</summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>Текущий путь файла (после перемещения совпадает с целевым).</summary>
    public string CurrentPath { get; set; } = string.Empty;

    /// <summary>
    /// Хэш содержимого (SHA-256 первых N КБ + размер, либо полный для мелких файлов) —
    /// для обнаружения дублей и защиты от повторной обработки (ARCHITECTURE §7.2).
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset DetectedAt { get; set; }

    public FileRecordStatus Status { get; set; }
}
