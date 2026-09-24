using StudComp.Core.Domain;

namespace StudComp.Core.Abstractions.Archivist;

/// <summary>
/// Решает, к какому предмету/папке относится обнаруженный файл, прогоняя правила по приоритету
/// (ARCHITECTURE §8.3, §8.4 п.3). Только принятие решения: файловую систему трогать нельзя.
/// </summary>
public interface ISortingRuleEngine
{
    /// <summary>
    /// Прогоняет <paramref name="rules"/> по <paramref name="file"/>, начиная с наибольшего
    /// <see cref="ArchivistRule.Priority"/>, и возвращает первое совпадение.
    /// </summary>
    /// <returns>
    /// Победившее решение либо <see langword="null"/>, если ничего не совпало. Такие файлы не пропадают
    /// молча — они попадают в раздел UI «Неразобранное» (ARCHITECTURE §8.4 п.7).
    /// </returns>
    Task<SortDecision?> EvaluateAsync(
        WatchedFileInfo file,
        IReadOnlyList<ArchivistRule> rules,
        CancellationToken ct);
}
