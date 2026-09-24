namespace StudComp.Modules.Archivist.Services;

/// <summary>Как накладывать импортируемые правила на текущий набор (ARCHITECTURE §8.7).</summary>
public enum RulesImportMode
{
    /// <summary>Стереть все текущие правила и поставить импортированные.</summary>
    Replace = 0,

    /// <summary>Добавить импортированные к существующим.</summary>
    Append = 1,
}

/// <summary>Итог импорта для показа пользователю.</summary>
/// <param name="Imported">Сколько правил добавлено.</param>
/// <param name="Skipped">Сколько правил пропущено (без условия, битый regex).</param>
/// <param name="Warnings">Некритичные замечания: ненайденные предметы и пропуски.</param>
public sealed record RulesImportSummary(int Imported, int Skipped, IReadOnlyList<string> Warnings);
