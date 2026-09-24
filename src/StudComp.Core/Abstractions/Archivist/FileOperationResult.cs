using StudComp.Core.Common;

namespace StudComp.Core.Abstractions.Archivist;

/// <summary>
/// Чем закончилось выполнение <see cref="SortDecision"/> (ARCHITECTURE §8.4 п.5–6, §8.6).
/// Члена «перезаписан» нет намеренно: архивариус не перезаписывает файлы пользователя.
/// </summary>
public enum FileOperationOutcome
{
    /// <summary>Файл перенесён и переименован в <see cref="FileOperationResult.FinalPath"/>.</summary>
    Moved = 0,

    /// <summary>В целевой папке уже лежит идентичное содержимое — исходник не тронут, помечен дублем.</summary>
    SkippedDuplicate = 1,

    /// <summary>Файл залочен или недоступен — остаётся на месте и возвращается в очередь ретраев.</summary>
    Deferred = 2,

    /// <summary>Операция не удалась, см. <see cref="FileOperationResult.Error"/>. Исходный файл цел.</summary>
    Failed = 3,
}

/// <summary>
/// Результат одной файловой операции архивариуса (ARCHITECTURE §8.3).
/// </summary>
/// <param name="FileRecordId">Запись <c>FileRecord</c>, к которой относится операция.</param>
/// <param name="Outcome">Что на самом деле произошло с файлом.</param>
/// <param name="FinalPath">Итоговый абсолютный путь, если файл переехал; иначе <see langword="null"/>.</param>
/// <param name="Error">Описание ошибки для <see cref="FileOperationOutcome.Deferred"/> и <see cref="FileOperationOutcome.Failed"/>.</param>
public record FileOperationResult(
    Guid FileRecordId,
    FileOperationOutcome Outcome,
    string? FinalPath,
    Error? Error)
{
    /// <summary>True, если файл оказался там, куда его отправляло решение.</summary>
    public bool Succeeded => Outcome is FileOperationOutcome.Moved;
}
