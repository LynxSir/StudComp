using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.ViewModels.ReportForge;

/// <summary>Макет выгрузки колоды в отчёт (new_addons.md §7.5).</summary>
public enum CardComposeLayout
{
    /// <summary>Вопрос заголовком третьего уровня, ответ абзацем.</summary>
    Materials = 0,

    /// <summary>Компактно: строка на карточку, минимальные поля и мелкий кегль профиля «Шпаргалка».</summary>
    CheatSheet = 1,
}

/// <summary>Строка карточки в диалоге «Собрать из карточек» — с чекбоксом выбора.</summary>
public sealed partial class CardSelectionRowViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public CardSelectionRowViewModel(Card card, Action selectionChanged)
    {
        _selectionChanged = selectionChanged;
        Id = card.Id;
        Front = card.Front;
        Back = card.Back;
    }

    public Guid Id { get; }

    public string Front { get; }

    public string Back { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}

/// <summary>
/// Диалог «Собрать из карточек» (new_addons.md §7.5): предмет (по умолчанию — уже выбранный в форме
/// отчёта) → опционально колода → мультивыбор карточек → склейка в markdown-редактор «Нового отчёта».
/// Списки предметов передаются уже загруженными из <see cref="NewReportViewModel"/> — тот же приём,
/// что у <see cref="ComposeFromNotesViewModel"/>.
/// </summary>
public sealed partial class ComposeFromCardsViewModel : ObservableObject
{
    private readonly ICardService _cards;
    private readonly ICardDeckService _decks;

    public ComposeFromCardsViewModel(
        IReadOnlyList<Subject> subjects,
        ICardService cards,
        ICardDeckService decks,
        Guid? preselectedSubjectId)
    {
        _cards = cards;
        _decks = decks;
        Subjects = subjects;
        _selectedLayout = Layouts[0];
        _selectedSubject = subjects.FirstOrDefault(s => s.Id == preselectedSubjectId) ?? subjects.FirstOrDefault();

        _ = LoadDecksAndCardsAsync();
    }

    public IReadOnlyList<Subject> Subjects { get; }

    public IReadOnlyList<NamedChoice<CardComposeLayout>> Layouts { get; } =
    [
        new(CardComposeLayout.Materials, "Материалы"),
        new(CardComposeLayout.CheatSheet, "Шпаргалка"),
    ];

    /// <summary>Колоды предмета плюс пункт «вся картотека предмета» первым.</summary>
    public ObservableCollection<CardDeck?> Decks { get; } = [];

    public ObservableCollection<CardSelectionRowViewModel> Cards { get; } = [];

    [ObservableProperty]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private CardDeck? _selectedDeck;

    [ObservableProperty]
    private NamedChoice<CardComposeLayout> _selectedLayout;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canSave;

    public bool HasCards => Cards.Count > 0;

    public CardComposeLayout Layout => SelectedLayout.Value;

    partial void OnSelectedSubjectChanged(Subject? value) => _ = LoadDecksAndCardsAsync();

    partial void OnSelectedDeckChanged(CardDeck? value) => _ = LoadCardsAsync();

    /// <summary>
    /// Склеить отмеченные карточки в markdown. «Материалы» — вопрос заголовком, ответ абзацем;
    /// «Шпаргалка» — компактная строка на карточку, разницу в вёрстке даёт клонированный профиль
    /// оформления (<see cref="CheatSheetTemplateProvider"/>), не сама разметка.
    /// </summary>
    public string BuildMarkdown() => Layout switch
    {
        CardComposeLayout.CheatSheet => string.Join(
            "\n",
            Cards.Where(c => c.IsSelected).Select(c => $"**{c.Front}** — {Flatten(c.Back)}\n")),
        _ => string.Join(
            "\n",
            Cards.Where(c => c.IsSelected).Select(c => $"### {c.Front}\n\n{c.Back}\n")),
    };

    private static string Flatten(string back) =>
        string.Join(' ', (back ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private async Task LoadDecksAndCardsAsync()
    {
        var subjectDecks = SelectedSubject is { } subject
            ? await _decks.GetBySubjectAsync(subject.Id).ConfigureAwait(true)
            : [];

        Decks.Clear();
        Decks.Add(null);
        foreach (var deck in subjectDecks)
        {
            Decks.Add(deck);
        }

        // Сброс колоды сам запускает LoadCardsAsync через OnSelectedDeckChanged; если колода и так
        // была пустой, события не будет — тогда грузим явно.
        if (SelectedDeck is null)
        {
            await LoadCardsAsync().ConfigureAwait(true);
        }
        else
        {
            SelectedDeck = null;
        }
    }

    // Номер последней запрошенной загрузки: ответ устаревшего запроса (быстро сменили колоду)
    // отбрасывается, а не дописывается к списку — запросы теперь идут на пуле и могут наложиться.
    private int _cardsLoadVersion;

    private async Task LoadCardsAsync()
    {
        var version = ++_cardsLoadVersion;
        Cards.Clear();
        CanSave = false;
        OnPropertyChanged(nameof(HasCards));

        if (SelectedSubject is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var list = SelectedDeck is { } deck
                ? await _decks.GetCardsAsync(deck.Id).ConfigureAwait(true)
                : await _cards.GetBySubjectAsync(SelectedSubject.Id).ConfigureAwait(true);

            if (version != _cardsLoadVersion)
            {
                return;
            }

            foreach (var card in list)
            {
                Cards.Add(new CardSelectionRowViewModel(card, UpdateCanSave));
            }

            OnPropertyChanged(nameof(HasCards));
        }
        finally
        {
            if (version == _cardsLoadVersion)
            {
                IsBusy = false;
            }
        }
    }

    private void UpdateCanSave() => CanSave = Cards.Any(c => c.IsSelected);
}
