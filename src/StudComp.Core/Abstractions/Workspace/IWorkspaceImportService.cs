using StudComp.Core.Common;

namespace StudComp.Core.Abstractions.Workspace;

/// <summary>Что делать с исходным файлом при импорте в папку предмета (new_addons.md §1.11).</summary>
public enum ImportMode
{
    /// <summary>Перенести: исходник исчезает, но только после успешного переноса.</summary>
    Move = 0,

    /// <summary>Скопировать: исходник не трогается никогда.</summary>
    Copy = 1,
}

/// <summary>
/// Что делать, если в целевой папке уже лежит файл с тем же содержимым. Решение принимает
/// вызывающий <b>до</b> запуска импорта: колбэк из инфраструктуры в UI сделал бы файловый сервис
/// зависимым от интерактивности, а он должен быть тестируемым насквозь.
/// </summary>
public enum DuplicatePolicy
{
    /// <summary>Пропустить — исходник и цель остаются как были.</summary>
    Skip = 0,

    /// <summary>Всё равно импортировать, рядом, с числовым суффиксом.</summary>
    KeepBoth = 1,
}

/// <summary>Исход импорта одного файла.</summary>
public enum ImportItemOutcome
{
    /// <summary>Файл лёг в целевую папку под своим именем.</summary>
    Imported = 0,

    /// <summary>Имя было занято другим файлом — импортирован с суффиксом <c> (2)</c>, <c> (3)</c>…</summary>
    RenamedDueToConflict = 1,

    /// <summary>В целевой папке уже лежит файл с тем же содержимым; ничего не тронуто.</summary>
    SkippedDuplicate = 2,

    /// <summary>Импортировать не удалось; исходный файл остался нетронутым.</summary>
    Failed = 3,
}

/// <summary>Результат импорта одного файла.</summary>
/// <param name="SourcePath">Откуда брали.</param>
/// <param name="FinalPath">Куда легло; <see langword="null"/> при <see cref="ImportItemOutcome.Failed"/>.</param>
/// <param name="ContentHash">Хэш содержимого — чтобы вызывающий не считал его второй раз.</param>
/// <param name="Error">Причина отказа; <see langword="null"/> при успехе.</param>
public sealed record ImportItemResult(
    string SourcePath,
    string? FinalPath,
    ImportItemOutcome Outcome,
    string? ContentHash,
    Error? Error);

/// <summary>Прогресс импорта — по файлам, не по байтам (см. замечание об отмене в сервисе).</summary>
public sealed record ImportProgress(int Done, int Total, string CurrentFileName);

/// <summary>Итог всей пачки.</summary>
public sealed record ImportSummary(IReadOnlyList<ImportItemResult> Items, bool WasCancelled)
{
    public int Imported => Items.Count(
        x => x.Outcome is ImportItemOutcome.Imported or ImportItemOutcome.RenamedDueToConflict);

    public int SkippedDuplicates => Items.Count(x => x.Outcome == ImportItemOutcome.SkippedDuplicate);

    public int Failed => Items.Count(x => x.Outcome == ImportItemOutcome.Failed);
}

/// <summary>
/// Импорт файлов из проводника в папку предмета (new_addons.md §1.11). Делает только файловую
/// работу — учёт (<c>FileRecord</c>, лента активности) ведёт вызывающий: <c>Infrastructure</c> не
/// ссылается на <c>Data</c> (ARCHITECTURE §5.1).
/// </summary>
/// <remarks>
/// Здесь действует то же самое строжайшее требование, что и в Архивариусе (§14): файл пользователя
/// не удаляется и не перезаписывается ни при каких условиях. Перемещение — только
/// <c>Move(overwrite: false)</c>, копирование — только <c>Copy(overwrite: false)</c>, метода
/// удаления в <c>IFileSystem</c> нет по конструкции (ADR §16.30).
/// </remarks>
public interface IWorkspaceImportService
{
    /// <summary>
    /// Импортирует файлы (и содержимое перетащенных папок) в <paramref name="targetDirectory"/>.
    /// Отмена проверяется <b>между файлами</b>: операция над отдельным файлом атомарна, поэтому
    /// оборванных «полуфайлов» не остаётся, но крупный файл прервать на середине нельзя.
    /// </summary>
    Task<ImportSummary> ImportAsync(
        IReadOnlyList<string> sourcePaths,
        string targetDirectory,
        ImportMode mode,
        DuplicatePolicy duplicates = DuplicatePolicy.Skip,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
