using StudComp.Core.Domain;

namespace StudComp.Core.Abstractions.Archivist;

/// <summary>
/// Что делать, если целевой путь уже занят (ARCHITECTURE §8.4 п.5, §8.6).
/// Варианта «перезаписать» здесь нет намеренно.
/// </summary>
public enum ConflictResolution
{
    /// <summary>Путь свободен, разрешать нечего.</summary>
    None = 0,

    /// <summary>Файл с таким именем есть, но содержимое другое — дописать <c>(2)</c>, <c>(3)</c>… к имени.</summary>
    AppendSuffix = 1,

    /// <summary>Файл с таким именем есть, хэш совпадает — оставить исходник на месте и пометить дублем.</summary>
    SkipDuplicateHash = 2,
}

/// <summary>
/// Вердикт движка правил по одному файлу: куда переносим и под каким именем.
/// Решение описывает только намерение — на диске ничего не меняется, пока его не выполнит
/// <see cref="IFileOperationExecutor.MoveAndRenameAsync"/> (ARCHITECTURE §8.3).
/// </summary>
/// <param name="SourceFileRecordId">Id уже записанного <c>FileRecord</c> для обнаруженного файла.</param>
/// <param name="SubjectId">Предмет, к которому отнесён файл, либо <see langword="null"/> для общего правила.</param>
/// <param name="TargetDirectory">Абсолютный путь папки, в которую переносится файл.</param>
/// <param name="NewFileName">Имя, собранное по <see cref="ArchivistRule.RenameTemplate"/>.</param>
/// <param name="MatchedRule">Правило, победившее в разборе по приоритету.</param>
/// <param name="Conflict">Выбранный способ разрешения конфликта имён.</param>
public record SortDecision(
    Guid SourceFileRecordId,
    Guid? SubjectId,
    string TargetDirectory,
    string NewFileName,
    ArchivistRule MatchedRule,
    ConflictResolution Conflict);
