using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Modules.Cards.Services;
// Только SymbolRegular: Wpf.Ui.Controls.Card конфликтует с доменной Card.
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Готовые списки для <c>ComboBox</c> раздела «Картотека» и пресеты рельса фильтров.
/// Аналог <c>ArchivistChoices</c> и <c>OrganizerChoices</c>.
/// </summary>
public static class CardChoices
{
    public static IReadOnlyList<NamedChoice<CardKind>> Kinds { get; } =
    [
        new(CardKind.Term, "Термин"),
        new(CardKind.Question, "Вопрос"),
        new(CardKind.Formula, "Формула"),
        new(CardKind.Code, "Код"),
        new(CardKind.Fact, "Факт"),
    ];

    public static IReadOnlyList<NamedChoice<CardDifficulty>> Difficulties { get; } =
    [
        new(CardDifficulty.Easy, "Лёгкая"),
        new(CardDifficulty.Normal, "Обычная"),
        new(CardDifficulty.Hard, "Трудная"),
    ];

    /// <summary>Порядок выдачи — переключатель в шапке библиотеки (new_addons.md §8.2).</summary>
    public static IReadOnlyList<NamedChoice<CardSortOrder>> Sorts { get; } =
    [
        new(CardSortOrder.Relevance, "По релевантности"),
        new(CardSortOrder.RecentlyUpdated, "Сначала изменённые"),
        new(CardSortOrder.RecentlyCreated, "Сначала новые"),
        new(CardSortOrder.Alphabetical, "По алфавиту"),
        new(CardSortOrder.DueDate, "По сроку повторения"),
    ];

    /// <summary>Способы проверки себя в сессии (new_addons.md §5.3).</summary>
    public static IReadOnlyList<NamedChoice<StudyCheckMode>> CheckModes { get; } =
    [
        new(StudyCheckMode.SelfAssessment, "Самооценка"),
        new(StudyCheckMode.MultipleChoice, "Выбор варианта"),
        new(StudyCheckMode.TypedAnswer, "Ввод ответа"),
        new(StudyCheckMode.Cloze, "Пропуски"),
    ];

    /// <summary>Пресеты размера билета; <c>0</c> — «всё, что подходит».</summary>
    public static IReadOnlyList<NamedChoice<int>> ExamSizes { get; } =
    [
        new(20, "20 карточек"),
        new(40, "40 карточек"),
        new(0, "Все подходящие"),
    ];

    /// <summary>
    /// Лимит времени. Положительное — секунды на всю сессию, отрицательное — на одну карточку,
    /// ноль — без ограничения.
    /// </summary>
    public static IReadOnlyList<NamedChoice<int>> TimeLimits { get; } =
    [
        new(0, "Без ограничения"),
        new(15 * 60, "15 минут на сессию"),
        new(30 * 60, "30 минут на сессию"),
        new(60 * 60, "Час на сессию"),
        new(-30, "30 секунд на карточку"),
        new(-60, "Минута на карточку"),
    ];

    /// <summary>Порядок очереди дня — настройка раздела (new_addons.md §11).</summary>
    public static IReadOnlyList<NamedChoice<StudyOrder>> QueueOrders { get; } =
    [
        new(StudyOrder.DueFirst, "Сначала просроченные"),
        new(StudyOrder.HardestFirst, "Сначала трудные"),
        new(StudyOrder.LeastRecentlySeen, "Давно не видел"),
        new(StudyOrder.Random, "Случайный"),
    ];

    /// <summary>Режимы импорта картотеки (new_addons.md §7.6).</summary>
    public static IReadOnlyList<NamedChoice<CardImportMode>> ImportModes { get; } =
    [
        new(CardImportMode.Add, "Добавить"),
        new(CardImportMode.ReplaceDeck, "Заменить колоду"),
        new(CardImportMode.Merge, "Объединить"),
    ];

    /// <summary>Периоды для вкладки «Статистика» (new_addons.md §6.5).</summary>
    public static IReadOnlyList<NamedChoice<int>> StatPeriods { get; } =
    [
        new(7, "7 дней"),
        new(30, "30 дней"),
        new(180, "180 дней"),
    ];

    /// <summary>
    /// Пресеты левого рельса. Каждый — просто текст, который дописывается в строку поиска, чтобы
    /// фильтр и поиск были одним механизмом, а не двумя (new_addons.md §8.2, §13.9).
    /// </summary>
    public static IReadOnlyList<CardFilterPreset> Presets { get; } =
    [
        new("Все карточки", string.Empty, SymbolRegular.Layer24),
        new("Закреплённые", "закреплённые", SymbolRegular.Pin24),
        new("К повторению", "сегодня", SymbolRegular.Clock24),
        new("Трудные", "трудные", SymbolRegular.Fire24),
        new("Новые", "новые", SymbolRegular.Sparkle24),
        new("Без предмета", "нет:предмета", SymbolRegular.BookQuestionMark24),
        new("Без меток", "нет:метки", SymbolRegular.TagDismiss24),
        new("Корзина", "корзина", SymbolRegular.Delete24),
    ];

    /// <summary>Подпись <see cref="CardKind"/> для строки списка и панели просмотра.</summary>
    public static string DisplayOf(CardKind kind) =>
        Kinds.FirstOrDefault(x => x.Value == kind)?.Display ?? kind.ToString();

    /// <summary>Подпись <see cref="CardDifficulty"/> для панели просмотра.</summary>
    public static string DisplayOf(CardDifficulty difficulty) =>
        Difficulties.FirstOrDefault(x => x.Value == difficulty)?.Display ?? difficulty.ToString();

    /// <summary>Иконка WPF-UI по виду карточки — единый outline-стиль (ARCHITECTURE §19.4).</summary>
    public static SymbolRegular IconOf(CardKind kind) => kind switch
    {
        CardKind.Question => SymbolRegular.QuestionCircle24,
        CardKind.Formula => SymbolRegular.MathFormula24,
        CardKind.Code => SymbolRegular.Code24,
        CardKind.Fact => SymbolRegular.Lightbulb24,
        _ => SymbolRegular.TextT24,
    };
}

/// <summary>Пункт рельса фильтров.</summary>
/// <param name="Title">Подпись.</param>
/// <param name="Query">Строка, которая станет запросом. Пустая — «показать всё».</param>
/// <param name="Icon">Иконка пункта.</param>
public sealed record CardFilterPreset(string Title, string Query, SymbolRegular Icon);
