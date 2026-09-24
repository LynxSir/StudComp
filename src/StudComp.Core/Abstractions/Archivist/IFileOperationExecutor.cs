namespace StudComp.Core.Abstractions.Archivist;

/// <summary>
/// Единственный компонент, которому позволено перемещать и переименовывать файлы пользователя
/// (ARCHITECTURE §8.3, §8.6). Контракт для любой реализации: никогда не удалять и не перезаписывать
/// существующий файл — потеря файла пользователя это то, чего проект не допускает вообще (ARCHITECTURE §14).
/// </summary>
public interface IFileOperationExecutor
{
    /// <summary>
    /// Выполняет <see cref="SortDecision"/>: сперва пишет запись в undo-лог, только потом переносит и
    /// переименовывает файл — чтобы падение между двумя шагами ловилось сверкой (ARCHITECTURE §8.4 п.6).
    /// </summary>
    Task<FileOperationResult> MoveAndRenameAsync(SortDecision decision, CancellationToken ct);

    /// <summary>
    /// Откатывает ранее выполненную операцию, возвращая файл на исходный путь.
    /// </summary>
    /// <returns><see langword="true"/>, если файл возвращён; <see langword="false"/>, если не вышло (пользователь его перенёс, целевой путь занят).</returns>
    Task<bool> UndoAsync(Guid fileRecordId, CancellationToken ct);
}
