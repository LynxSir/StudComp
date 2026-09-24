using StudComp.Core.Common;
using StudComp.Core.Domain;

namespace StudComp.Modules.Archivist.Services;

/// <summary>Одна строка журнала операций для вкладки «Журнал» (ARCHITECTURE §8.6).</summary>
/// <param name="FileRecordId">Запись учёта файла — по ней делается отмена.</param>
/// <param name="FileName">Текущее имя файла.</param>
/// <param name="OriginalPath">Откуда файл забрали.</param>
/// <param name="FinalPath">Куда положили (или <see langword="null"/> для неуспешной операции).</param>
/// <param name="StartedAt">Когда операция началась.</param>
/// <param name="Status">Итог операции.</param>
/// <param name="CanUndo">Можно ли отменить прямо сейчас (файл на месте, прежний путь свободен).</param>
public sealed record OperationHistoryItem(
    Guid FileRecordId,
    string FileName,
    string OriginalPath,
    string? FinalPath,
    DateTimeOffset StartedAt,
    FileOperationLogStatus Status,
    bool CanUndo);

/// <summary>
/// Данные и отмена для вкладки «Журнал» раздела «Архивариус»: последние N файловых операций и кнопка
/// «Отменить» у каждой (ARCHITECTURE §8.6, PLAN Phase 8 — «отмена на последние N сортировок»).
/// </summary>
public interface IOperationHistoryService
{
    Task<IReadOnlyList<OperationHistoryItem>> GetRecentAsync(int take = 50, CancellationToken ct = default);

    Task<Result> UndoAsync(Guid fileRecordId, CancellationToken ct = default);
}
