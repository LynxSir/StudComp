using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Предмет в рельсе фильтров: сколько у него карточек и какие колоды внутри (new_addons.md §8.2).
/// </summary>
public sealed partial class CardSubjectNodeViewModel : ObservableObject
{
    public CardSubjectNodeViewModel(Subject subject, int count, IReadOnlyList<CardDeckNodeViewModel> decks)
    {
        Subject = subject;
        Count = count;
        Decks = decks;
    }

    public Subject Subject { get; }

    public string Title => Subject.Name;

    /// <summary>Токен, который уходит в строку поиска по клику. Имя в кавычках — оно бывает из двух слов.</summary>
    public string Query => $"предмет:\"{Subject.Name}\"";

    public int Count { get; }

    public IReadOnlyList<CardDeckNodeViewModel> Decks { get; }

    public bool HasDecks => Decks.Count > 0;

    /// <summary>Раскрыт ли предмет — колоды показываются вложенными, а не отдельным списком.</summary>
    [ObservableProperty]
    private bool _isExpanded;
}

/// <summary>Колода или умная подборка в рельсе фильтров.</summary>
public sealed class CardDeckNodeViewModel
{
    public CardDeckNodeViewModel(CardDeck deck, int count)
    {
        Deck = deck;
        Count = count;
    }

    public CardDeck Deck { get; }

    public string Title => Deck.Name;

    /// <summary>
    /// У обычной колоды считается ссылками, у умной подборки — запросом, поэтому число для неё
    /// заранее не известно и не показывается.
    /// </summary>
    public int Count { get; }

    /// <summary>Умная подборка — это колода с сохранённым запросом (new_addons.md §13.8).</summary>
    public bool IsSmart => !string.IsNullOrWhiteSpace(Deck.QueryExpression);

    public bool ShowCount => !IsSmart;

    /// <summary>Токен для строки поиска: подборка подставляет свой запрос, обычная колода — имя.</summary>
    public string Query => IsSmart
        ? Deck.QueryExpression!.Trim()
        : $"колода:\"{Deck.Name}\"";

    public string Description => Deck.Description ?? string.Empty;
}
