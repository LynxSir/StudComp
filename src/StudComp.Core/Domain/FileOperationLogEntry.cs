namespace StudComp.Core.Domain;

/// <summary>
/// Состояние записи журнала файловых операций (ARCHITECTURE §8.6).
/// </summary>
public enum FileOperationLogStatus
{
    /// <summary>Операция запланирована и записана, но ещё не выполнена — запись сделана до обращения к диску.</summary>
    Planned = 0,

    /// <summary>Файл успешно перемещён, <c>FinalPath</c> заполнен.</summary>
    Completed = 1,

    /// <summary>Операция не удалась (файл залочен, нет прав и т.п.); файл при этом остался на месте.</summary>
    Failed = 2,

    /// <summary>
    /// Успешную операцию отменил пользователь через Undo: файл возвращён на <c>OriginalPath</c>
    /// (ARCHITECTURE §8.6, Phase 8). Повторно отменить такую запись нельзя.
    /// </summary>
    RolledBack = 3,
}

/// <summary>
/// Журнал файловых операций архивариуса (ARCHITECTURE §7.1 <c>FILE_OPERATION_LOG</c>, §8.6).
/// Запись со статусом <see cref="FileOperationLogStatus.Planned"/> создаётся <b>до</b> фактического
/// перемещения — если приложение упадёт между записью и операцией, расхождение видно по журналу
/// (crash-safety, ARCHITECTURE §8.4 п.6). Он же — источник данных для Undo: по последней
/// <see cref="FileOperationLogStatus.Completed"/>-записи файл возвращается на <see cref="OriginalPath"/>.
/// POCO без знания о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class FileOperationLogEntry
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Запись учёта файла, к которой относится операция.</summary>
    public Guid FileRecordId { get; set; }

    /// <summary>Правило, по которому принято решение; <see langword="null"/> при ручной сортировке.</summary>
    public Guid? RuleId { get; set; }

    /// <summary>Путь, с которого файл перемещается.</summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>Путь, по которому файл планируется положить (до разрешения конфликта имён).</summary>
    public string PlannedPath { get; set; } = string.Empty;

    /// <summary>Фактический путь после операции; заполняется только при успехе.</summary>
    public string? FinalPath { get; set; }

    /// <summary>Момент записи журнала — всегда раньше самой операции.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Момент завершения операции; <see langword="null"/>, пока статус <c>Planned</c>.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public FileOperationLogStatus Status { get; set; }
}
