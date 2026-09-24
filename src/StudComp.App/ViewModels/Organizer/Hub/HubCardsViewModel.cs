using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels.Cards;
using StudComp.ViewModels.Shell;

namespace StudComp.ViewModels.Organizer.Hub;

/// <summary>
/// Вкладка «Карточки» Хаба предмета (new_addons.md §2.3): карточки и колоды только этого предмета,
/// поиск внутри предмета, счётчики состояния и быстрое создание, не покидая Хаб.
/// </summary>
/// <remarks>
/// <para>
/// Читать сервисы Картотеки из вьюмодели Органайзера можно: межмодульной ссылки от этого не
/// возникает, склейка происходит в App-слое — тем же приёмом, каким «Собрать из заметок» в Отчётах
/// читает <c>INoteService</c> (new_addons.md §10.3).
/// </para>
/// <para>
/// Фильтрация идёт в памяти по уже загруженным карточкам предмета, а не запросом на каждую букву:
/// у одного предмета их десятки, и лишний поход в базу здесь ничего не купил бы. Полноценный поиск
/// со всеми операторами живёт в разделе — кнопка «Открыть в Картотеке» уводит туда с готовым
/// фильтром по предмету.
/// </para>
/// </remarks>
public sealed partial class HubCardsViewModel(
    ICardService cards,
    ICardDeckService decks,
    ICardTagService tags,
    ISubjectService subjects,
    ICramPlanService cram,
    INavigationService navigation,
    IDialogService dialogs,
    IToastService toasts,
    IMessenger messenger) : ObservableObject
{
    private Guid _subjectId;
    private Subject? _subject;
    private IReadOnlyList<Card> _all = [];
    private IReadOnlyDictionary<Guid, IReadOnlyList<CardTag>> _tagMap =
        new Dictionary<Guid, IReadOnlyList<CardTag>>();

    /// <summary>Карточки предмета после фильтра: закреплённые сверху, дальше по свежести правки.</summary>
    public ObservableCollection<CardRowViewModel> Items { get; } = [];

    /// <summary>Колоды предмета со счётчиками.</summary>
    public ObservableCollection<CardDeckNodeViewModel> Decks { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    private int _shownCount;

    [ObservableProperty]
    private int _cardCount;

    [ObservableProperty]
    private int _newCount;

    [ObservableProperty]
    private int _dueCount;

    /// <summary>Поиск внутри предмета.</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public bool HasItems => ShownCount > 0;

    /// <summary>Есть ли у предмета карточки вообще — от этого зависит текст пустого состояния.</summary>
    public bool HasAnyCards => CardCount > 0;

    public async Task LoadAsync(Guid subjectId)
    {
        _subjectId = subjectId;

        try
        {
            IsBusy = true;

            _subject = await subjects.GetByIdAsync(subjectId).ConfigureAwait(true);
            _all = await cards.GetBySubjectAsync(subjectId).ConfigureAwait(true);
            _tagMap = await tags
                .GetForCardsAsync(_all.Select(x => x.Id).ToList())
                .ConfigureAwait(true);

            var now = DateTimeOffset.Now;
            CardCount = _all.Count;
            NewCount = _all.Count(x => x.DueAt is null);
            DueCount = _all.Count(x => !x.IsSuspended && x.DueAt is { } due && due <= now);
            OnPropertyChanged(nameof(HasAnyCards));

            var deckCounts = await cards.CountsByDeckAsync().ConfigureAwait(true);
            var subjectDecks = await decks.GetBySubjectAsync(subjectId).ConfigureAwait(true);

            // Коллекция заменяется одним синхронным блоком после последнего await: запросы теперь идут
            // на пуле, и наложившиеся загрузки иначе давали бы дубли (Phase 13.10).
            Decks.Clear();
            foreach (var deck in subjectDecks)
            {
                Decks.Add(new CardDeckNodeViewModel(deck, deckCounts.GetValueOrDefault(deck.Id)));
            }

            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    /// <summary>Создать карточку, уже привязанную к этому предмету.</summary>
    [RelayCommand]
    private async Task NewCardAsync()
    {
        var allSubjects = await subjects.GetAllAsync().ConfigureAwait(true);
        var allDecks = await decks.GetAllAsync().ConfigureAwait(true);
        var allTags = await tags.GetAllAsync().ConfigureAwait(true);

        var editor = new CardEditorViewModel(
            null, allSubjects, allDecks, allTags, cards, _subjectId, dialogs: dialogs);
        if (!await dialogs.ShowEditorAsync(editor, editor.HeaderText).ConfigureAwait(true))
        {
            return;
        }

        var result = await cards.CreateAsync(editor.ToModel(), editor.ParseTags()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось создать карточку", result.Error.Message, ToastKind.Error);
            return;
        }

        messenger.Send(new CardsChangedMessage());
        await LoadAsync(_subjectId).ConfigureAwait(true);
    }

    /// <summary>Открыть карточку в разделе — там она правится и живёт полноценно.</summary>
    [RelayCommand]
    private void OpenCard(CardRowViewModel? row)
    {
        if (row is not null)
        {
            navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(row.Id));
        }
    }

    /// <summary>Перейти в раздел «Картотека» с готовым фильтром по этому предмету.</summary>
    [RelayCommand]
    private void OpenLibrary() =>
        navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(Query: SubjectFilter()));

    /// <summary>
    /// Тренировать предмет — сессия по всем его карточкам. Кнопки не было в 12.7 ровно потому, что
    /// тренажёра ещё не существовало (new_addons.md §2.3).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasAnyCards))]
    private void Train() =>
        navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Practice,
            StudySessionFilter.Empty with { SubjectIds = [_subjectId] },
            Title: _subject is null ? "Тренировка" : $"Тренировка · {_subject.Name}"));

    /// <summary>Готовиться к экзамену по предмету — режим аврала (new_addons.md §6.4).</summary>
    [RelayCommand]
    private async Task PrepareForExamAsync()
    {
        var today = await cram.GetTodayAsync(_subjectId).ConfigureAwait(true);

        if (today.IsFailure)
        {
            toasts.Show("Аврал", today.Error.Message, ToastKind.Warning);
            return;
        }

        navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Cram,
            StudySessionFilter.Empty with { CardIds = today.Value, Order = StudyOrder.HardestFirst },
            Title: _subject is null ? "Аврал" : $"Аврал · {_subject.Name}"));
    }

    /// <summary>Открыть колоду в разделе.</summary>
    [RelayCommand]
    private void OpenDeck(CardDeckNodeViewModel? node)
    {
        if (node is not null)
        {
            navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(Query: node.Query));
        }
    }

    private string SubjectFilter() =>
        _subject is null ? string.Empty : $"предмет:\"{_subject.Name}\"";

    /// <summary>
    /// Отфильтровать загруженные карточки по строке поиска. Разбор — тем же <see cref="CardQuery"/>,
    /// что и в разделе, поэтому «#метка» и «тип:формула» работают и здесь.
    /// </summary>
    private void ApplyFilter()
    {
        var spec = CardQuery.Parse(Query);
        var terms = spec.Terms;

        Items.Clear();
        foreach (var card in _all)
        {
            var cardTags = _tagMap.GetValueOrDefault(card.Id, []);
            if (!Matches(card, cardTags, spec))
            {
                continue;
            }

            Items.Add(new CardRowViewModel(
                card,
                _subject?.Name,
                _subject?.ColorHex,
                cardTags,
                string.Empty,
                terms));
        }

        ShownCount = Items.Count;
        SummaryText = Describe();
    }

    private static bool Matches(Card card, IReadOnlyList<CardTag> cardTags, CardQuerySpec spec)
    {
        if (spec.Kind is { } kind && card.Kind != kind)
        {
            return false;
        }

        if (spec.Difficulty is { } difficulty && card.Difficulty != difficulty)
        {
            return false;
        }

        foreach (var tag in spec.Tags)
        {
            if (!cardTags.Any(x => string.Equals(x.Name, tag, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        if (spec.Flags.HasFlag(CardQueryFlags.Pinned) && !card.IsPinned)
        {
            return false;
        }

        if (spec.Flags.HasFlag(CardQueryFlags.New) && card.DueAt is not null)
        {
            return false;
        }

        if (spec.Flags.HasFlag(CardQueryFlags.Hard) && card.Lapses == 0)
        {
            return false;
        }

        var haystack = Fold($"{card.Front} {card.Back} {string.Join(' ', cardTags.Select(x => x.Name))}");

        foreach (var term in spec.Terms)
        {
            var needle = Fold(term.Text);
            var found = needle.Length == 0 || haystack.Contains(needle, StringComparison.Ordinal);

            // Исключающий терм работает наоборот: нашли — значит карточка не подходит.
            if (term.Kind == CardQueryTermKind.Exclude ? found : !found)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Складывание регистра и «ё» — то же правило, что в индексе и в подсветке.</summary>
    private static string Fold(string value) =>
        value.ToLowerInvariant().Replace('ё', 'е');

    private string Describe()
    {
        if (CardCount == 0)
        {
            return "По этому предмету карточек пока нет.";
        }

        var shown = ShownCount == CardCount
            ? Plural(CardCount)
            : $"{ShownCount} из {CardCount}";

        var parts = new List<string> { shown };
        if (NewCount > 0)
        {
            parts.Add($"новых: {NewCount}");
        }

        if (DueCount > 0)
        {
            parts.Add($"к повторению: {DueCount}");
        }

        if (Decks.Count > 0)
        {
            parts.Add($"колод: {Decks.Count}");
        }

        return string.Join(" · ", parts);
    }

    private static string Plural(int count) => count switch
    {
        1 => "1 карточка",
        _ when count % 10 is >= 2 and <= 4 && count % 100 is < 11 or > 14 => $"{count} карточки",
        _ => $"{count} карточек",
    };
}
