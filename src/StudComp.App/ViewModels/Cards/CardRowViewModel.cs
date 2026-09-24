using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.ViewModels.Organizer;
// Только SymbolRegular: Wpf.Ui.Controls.Card конфликтует с доменной Card.
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace StudComp.ViewModels.Cards;

/// <summary>Состояние повторения одной точкой — цвет задаётся стилем плитки по этому значению.</summary>
public enum CardReviewState
{
    /// <summary>Ни разу не показанная.</summary>
    New = 0,

    /// <summary>В работе: срок повторения ещё впереди.</summary>
    Scheduled = 1,

    /// <summary>Срок подошёл или прошёл.</summary>
    Due = 2,
}

/// <summary>
/// Плитка карточки в библиотеке: обе стороны с подсветкой совпадений, метки, цветная полоса
/// предмета, иконка вида и состояние повторения (new_addons.md §8.2).
/// </summary>
public sealed partial class CardRowViewModel : ObservableObject
{
    /// <summary>Сколько символов оборота показывать в плитке — дальше всё равно не влезает.</summary>
    private const int PreviewLength = 160;

    public CardRowViewModel(
        Card card,
        string? subjectName,
        string? subjectColorHex,
        IReadOnlyList<CardTag> tags,
        string snippet = "",
        IReadOnlyList<CardQueryTerm>? terms = null)
    {
        Card = card;
        SubjectName = subjectName ?? string.Empty;
        AccentBrush = SubjectColor.BrushFor(subjectColorHex);
        Tags = tags.Select(x => x.DisplayName).ToArray();

        FrontSegments = CardHighlight.FromTerms(card.Front, terms);

        // Оборот индекс уже разметил сам; если отрывка нет (запрос без слов или деградированный
        // режим §4.3) — подсвечиваем по термам то, что показываем.
        BackPreview = Flatten(card.Back);
        BackSegments = snippet.Length > 0
            ? CardHighlight.FromSnippet(snippet)
            : CardHighlight.FromTerms(BackPreview, terms, PreviewLength);
    }

    public Card Card { get; }

    public Guid Id => Card.Id;

    public string Front => Card.Front;

    /// <summary>Лицевая сторона, разрезанная на подсвеченные и обычные куски.</summary>
    public IReadOnlyList<CardTextSegment> FrontSegments { get; }

    /// <summary>Начало оборота теми же кусками.</summary>
    public IReadOnlyList<CardTextSegment> BackSegments { get; }

    /// <summary>Оборот одной строкой — тултип и палитра быстрого поиска.</summary>
    public string BackPreview { get; }

    public string SubjectName { get; }

    public Brush AccentBrush { get; }

    public IReadOnlyList<string> Tags { get; }

    public bool HasTags => Tags.Count > 0;

    public bool HasSubject => SubjectName.Length > 0;

    public SymbolRegular KindIcon => CardChoices.IconOf(Card.Kind);

    public string KindTitle => CardChoices.DisplayOf(Card.Kind);

    public bool IsPinned => Card.IsPinned;

    public bool IsSuspended => Card.IsSuspended;

    public bool IsDeleted => Card.DeletedAt is not null;

    /// <summary>Состояние повторения: новая, в работе или просрочена.</summary>
    public CardReviewState ReviewState => Card.DueAt switch
    {
        null => CardReviewState.New,
        var due when due <= DateTimeOffset.Now => CardReviewState.Due,
        _ => CardReviewState.Scheduled,
    };

    public string ReviewStateTitle => Card.DueAt switch
    {
        null => "Новая",
        var due when due <= DateTimeOffset.Now => "Пора повторить",
        var due => $"Повторение {due.Value.LocalDateTime:d MMMM}",
    };

    /// <summary>Выбрана ли строка — мультивыбор и панель массовых операций (new_addons.md §8.2).</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Схлопнуть переносы строк: в плитке текст живёт в две строки, а не в двадцать.</summary>
    private static string Flatten(string? text) => string.Join(
        ' ',
        (text ?? string.Empty).Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
