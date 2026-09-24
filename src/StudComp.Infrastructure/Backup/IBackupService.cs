using StudComp.Core.Common;
using StudComp.Core.Domain;

namespace StudComp.Infrastructure.Backup;

/// <summary>
/// Резервное копирование и восстановление данных Rubrica (new_addons.md §7 §9): один zip-архив с
/// файлом БД, <c>usersettings.json</c> и манифестом. Оркестрирует
/// <see cref="Core.Abstractions.Data.IDatabaseBackupPort"/> (снимок/замена БД) + упаковку; сам EF/Sqlite
/// не трогает.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Создаёт архив по указанному пути. Существующий файл заменяется атомарно (запись во временный +
    /// переименование), так что отмена или сбой не повреждают предыдущую копию.
    /// </summary>
    Task<Result> BackupAsync(
        string destinationZipPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Восстанавливает БД и настройки из архива. Перед заменой делает датированную резервную копию
    /// текущей БД. Возвращает <see cref="BackupRestoreOutcome"/>; при <see cref="BackupRestoreOutcome.RestartRequired"/>
    /// приложение нужно перезапустить. При любой ошибке текущие данные остаются нетронутыми.
    /// </summary>
    Task<Result<BackupRestoreOutcome>> RestoreAsync(
        string sourceZipPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default,
        string? studyParentDirectory = null);

    /// <summary>
    /// Быстрый read-only разбор архива (Phase 13.8, new_addons.md §13.2): читает только
    /// <c>manifest.json</c>, сверяет линию/версию с локальной установкой, ничего не извлекает и не
    /// заменяет. Нужен, чтобы показать пользователю информированное подтверждение перед
    /// <see cref="RestoreAsync"/>, а не молча заменять данные.
    /// </summary>
    Task<Result<BackupInspection>> InspectAsync(string sourceZipPath, CancellationToken ct = default);
}

/// <summary>Ход операции резервного копирования: человекочитаемая стадия + доля выполнения [0..1].</summary>
public readonly record struct BackupProgress(string Stage, double Fraction);

/// <summary>Итог восстановления: нужен ли перезапуск и куда сложена страховочная копия прежней БД.</summary>
public sealed record BackupRestoreOutcome(bool RestartRequired, string SafetyCopyPath, string? StudyRootPath = null);

/// <summary>
/// Результат разбора архива без его применения: линия/версия из манифеста архива, линия/версия
/// текущей установки и уже готовая классификация (<see cref="BackupLineageComparison"/>) — UI строит
/// подтверждение по <see cref="Relation"/>, не дублируя сравнение.
/// </summary>
public sealed record BackupInspection(
    bool HasManifest,
    Guid LineageId,
    int Version,
    DateTimeOffset CreatedUtc,
    string AppVersion,
    Guid LocalLineageId,
    int LocalVersion,
    BackupLineageRelation Relation,
    bool IncludesFiles = false);
