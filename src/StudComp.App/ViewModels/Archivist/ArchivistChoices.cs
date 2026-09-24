using StudComp.Core.Domain;

namespace StudComp.ViewModels.Archivist;

/// <summary>Готовые списки вариантов для выпадающих списков форм Архивариуса.</summary>
public static class ArchivistChoices
{
    /// <summary>Типы правил, доступные пользователю в форме редактора.</summary>
    public static IReadOnlyList<NamedChoice<RuleMatchType>> MatchTypes { get; } =
    [
        new(RuleMatchType.Extension, "По расширению файла"),
        new(RuleMatchType.Keyword, "По слову в имени файла"),
        new(RuleMatchType.Regex, "По регулярному выражению"),
    ];

    /// <summary>Русская подпись типа правила — в том числе для правил, созданных не через UI.</summary>
    public static string MatchTypeName(RuleMatchType matchType) => matchType switch
    {
        RuleMatchType.Extension => "Расширение",
        RuleMatchType.Keyword => "Слово в имени",
        _ => "Регулярное выражение",
    };

    /// <summary>Фильтр по дате в Журнале (Phase 12.2).</summary>
    public static IReadOnlyList<NamedChoice<LogDateFilter>> DateFilters { get; } =
    [
        new(LogDateFilter.All, "За всё время"),
        new(LogDateFilter.Today, "Сегодня"),
        new(LogDateFilter.Last7Days, "Последние 7 дней"),
        new(LogDateFilter.Last30Days, "Последние 30 дней"),
    ];

    /// <summary>Фильтр по исходу операции в Журнале (Phase 12.2).</summary>
    public static IReadOnlyList<NamedChoice<LogOutcomeFilter>> OutcomeFilters { get; } =
    [
        new(LogOutcomeFilter.All, "Любой исход"),
        new(LogOutcomeFilter.Moved, "Перемещены"),
        new(LogOutcomeFilter.Failed, "Не удалось"),
        new(LogOutcomeFilter.RolledBack, "Отменены"),
    ];
}
