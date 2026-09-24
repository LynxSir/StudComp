namespace StudComp.Core.Domain;

/// <summary>
/// Итог сравнения резервной копии с текущей локальной установкой (Phase 13.8, new_addons.md §13).
/// </summary>
public enum BackupLineageRelation
{
    /// <summary>Та же линия копий, версия в архиве новее локальной — обычное восстановление вперёд.</summary>
    SameLineageNewer,

    /// <summary>Та же линия, но версия в архиве не новее локальной — откат назад.</summary>
    SameLineageOlderOrEqual,

    /// <summary>Другая установка либо архив без сведений о линии (легаси/повреждённый манифест).</summary>
    DifferentOrUnknownLineage,
}

/// <summary>
/// Классификация резервной копии относительно текущей установки — чистое сравнение без побочных
/// эффектов, чтобы решение «что показать пользователю перед заменой» было таблично протестировано,
/// а не разбросано по <c>BackupService</c>/VM (конвенция проекта: сравнение — в <c>Core</c>,
/// прецеденты <see cref="TokenSimilarity"/>, <see cref="WeekParityCalculator"/>).
/// </summary>
public static class BackupLineageComparison
{
    /// <summary>
    /// Пустой либо расходящийся с локальным идентификатор линии всегда трактуется как
    /// «другая/неизвестная установка» (new_addons.md §13.2: «другой/отсутствующий ID → явное
    /// предупреждение»), а не попытка угадать намерение.
    /// </summary>
    public static BackupLineageRelation Classify(
        Guid remoteLineageId, int remoteVersion, Guid localLineageId, int localVersion)
    {
        if (remoteLineageId != Guid.Empty && remoteLineageId == localLineageId)
        {
            return remoteVersion > localVersion
                ? BackupLineageRelation.SameLineageNewer
                : BackupLineageRelation.SameLineageOlderOrEqual;
        }

        return BackupLineageRelation.DifferentOrUnknownLineage;
    }
}
