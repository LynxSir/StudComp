using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels;
using StudComp.ViewModels.Cards;
// Только SymbolRegular: Wpf.Ui.Controls.Card конфликтует с доменной Card.
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace StudComp.Controls;

/// <summary>
/// Правая панель библиотеки (new_addons.md §8.2): карточка целиком — и просмотр, и правка на месте.
/// </summary>
/// <remarks>
/// <para>
/// Кнопки «Сохранить» нет: правка уходит в БД сама, дебаунсом, как в <see cref="NoteEditorViewModel"/>.
/// Модальный редактор остался только для «Новой карточки» — когда карточек сотни, каждый диалог
/// ради одной буквы это налог на работу.
/// </para>
/// <para>
/// Содержимое и метки пишутся по отдельности: текст уходит через <c>UpdateContentAsync</c>, метки —
/// через <c>SetTagsAsync</c>, когда меняются чипы. Иначе каждое нажатие клавиши перетряхивало бы
/// связки и счётчики меток.
/// </para>
/// </remarks>
public sealed partial class CardPreviewViewModel : ObservableObject, IDisposable
{
    /// <summary>Пауза после последнего нажатия клавиши, после которой карточка уходит в БД.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// Пауза перед загрузкой выбранной карточки: пока пользователь пролистывает список стрелками,
    /// ходить в базу на каждую строку незачем.
    /// </summary>
    private static readonly TimeSpan LoadDelay = TimeSpan.FromMilliseconds(120);

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private readonly ICardService _cards;
    private readonly ICardTagService _tags;
    private readonly ICardDeckService _decks;
    private readonly ISubjectService _subjects;
    private readonly IMarkdownDocumentModelBuilder _markdown;
    private readonly INavigationService _navigation;
    private readonly IToastService _toasts;
    private readonly IDialogService _dialogs;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _saveCts;

    private Card? _card;
    private bool _loading;
    private bool _dirty;
    private bool _tagsDirty;
    private bool _listsLoaded;

    public CardPreviewViewModel(
        ICardService cards,
        ICardTagService tags,
        ICardDeckService decks,
        ISubjectService subjects,
        IMarkdownDocumentModelBuilder markdown,
        INavigationService navigation,
        IToastService toasts,
        IDialogService dialogs)
    {
        _cards = cards;
        _tags = tags;
        _decks = decks;
        _subjects = subjects;
        _markdown = markdown;
        _navigation = navigation;
        _toasts = toasts;
        _dialogs = dialogs;

        Tags.CollectionChanged += OnTagsChanged;
    }

    /// <summary>Карточка сохранена — библиотеке пора обновить плитку.</summary>
    public event EventHandler? Saved;

    /// <summary>Карточка уехала в корзину или вернулась — выдачу надо перечитать целиком.</summary>
    public event EventHandler? Removed;

    /// <summary>Открыта ли карточка. Нет — панель показывает подсказку, а не пустоту.</summary>
    [ObservableProperty]
    private bool _hasCard;

    /// <summary>Режим правки. Просмотр показывает оборот размеченным, правка — исходный текст.</summary>
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _front = string.Empty;

    [ObservableProperty]
    private string _back = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private CardDeck? _selectedDeck;

    [ObservableProperty]
    private NamedChoice<CardKind>? _selectedKind;

    [ObservableProperty]
    private NamedChoice<CardDifficulty>? _selectedDifficulty;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isSuspended;

    [ObservableProperty]
    private bool _isDeleted;

    /// <summary>Оборот, отрисованный как Markdown — тот же построитель, что у заметок и отчётов.</summary>
    [ObservableProperty]
    private FlowDocument? _backDocument;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Состояние повторения человеческими словами.</summary>
    [ObservableProperty]
    private string _reviewText = string.Empty;

    /// <summary>Когда создана и когда правилась.</summary>
    [ObservableProperty]
    private string _metaText = string.Empty;

    [ObservableProperty]
    private bool _hasHint;

    [ObservableProperty]
    private bool _hasSource;

    /// <summary>Метки карточки чипами — правятся прямо в панели.</summary>
    public ObservableCollection<string> Tags { get; } = [];

    /// <summary>Предметы плюс пункт «без предмета» первым.</summary>
    public ObservableCollection<Subject?> Subjects { get; } = [];

    /// <summary>Колоды плюс пункт «без колоды» первым.</summary>
    public ObservableCollection<CardDeck?> Decks { get; } = [];

    /// <summary>Существующие метки — автодополнение чипов.</summary>
    public ObservableCollection<string> TagSuggestions { get; } = [];

    public IReadOnlyList<NamedChoice<CardKind>> Kinds => CardChoices.Kinds;

    public IReadOnlyList<NamedChoice<CardDifficulty>> Difficulties => CardChoices.Difficulties;

    /// <summary>Открытая карточка; <see cref="Guid.Empty"/> — панель пуста.</summary>
    public Guid CardId => _card?.Id ?? Guid.Empty;

    partial void OnFrontChanged(string value) => ScheduleSave();

    partial void OnBackChanged(string value) => ScheduleSave();

    partial void OnHintChanged(string value) => ScheduleSave();

    partial void OnSourceChanged(string value) => ScheduleSave();

    partial void OnSelectedSubjectChanged(Subject? value) => ScheduleSave();

    partial void OnSelectedDeckChanged(CardDeck? value) => ScheduleSave();

    /// <summary>Иконка вида карточки — шапка панели.</summary>
    public SymbolRegular KindIcon => CardChoices.IconOf(SelectedKind?.Value ?? CardKind.Term);

    partial void OnSelectedKindChanged(NamedChoice<CardKind>? value)
    {
        OnPropertyChanged(nameof(KindIcon));
        ScheduleSave();
    }

    partial void OnSelectedDifficultyChanged(NamedChoice<CardDifficulty>? value) => ScheduleSave();

    partial void OnIsEditingChanged(bool value)
    {
        if (!value)
        {
            // Уход из правки — это не «отмена»: дописываем и сразу показываем размеченный оборот.
            _ = FlushAsync();
            BackDocument = CardWikiLinkRenderer.Render(_markdown, Back, OnWikiLinkClicked);
        }
    }

    /// <summary>
    /// Показать карточку. Несохранённое от предыдущей дописывается до конца, поэтому быстрый
    /// перебор списка стрелками ничего не теряет.
    /// </summary>
    public void Show(Guid? cardId)
    {
        _loadCts?.Cancel();

        if (cardId is not { } id || id == Guid.Empty)
        {
            _ = ClearAsync();
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        _ = DelayThenLoadAsync(id, cts.Token);
    }

    /// <summary>Немедленно записать несохранённое — смена карточки, потеря фокуса, уход со страницы.</summary>
    public async Task FlushAsync()
    {
        CancelPendingSave();

        if (_card is null)
        {
            return;
        }

        if (_dirty)
        {
            await SaveContentAsync().ConfigureAwait(true);
        }

        if (_tagsDirty)
        {
            await SaveTagsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Перечитать списки предметов, колод и меток — после правок в рельсе библиотеки.</summary>
    public async Task RefreshListsAsync()
    {
        _listsLoaded = false;
        await EnsureListsAsync().ConfigureAwait(true);
    }

    /// <summary>Начать правку — кнопка «Править» и клавиша <c>E</c>.</summary>
    /// <summary>Шпаргалка по разметке (new_addons.md §11.4): оборот карточки — тоже Markdown.</summary>
    [RelayCommand]
    private Task ShowMarkdownHelpAsync() =>
        _dialogs.ShowInfoAsync(new MarkdownHelpViewModel(), "Разметка Markdown");

    [RelayCommand]
    private void BeginEdit()
    {
        if (HasCard && !IsDeleted)
        {
            IsEditing = true;
        }
    }

    /// <summary>Закончить правку — <c>Ctrl+Enter</c> и уход фокуса.</summary>
    [RelayCommand]
    private async Task EndEditAsync()
    {
        IsEditing = false;
        await FlushAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        if (_card is null)
        {
            return;
        }

        IsPinned = !IsPinned;
        await _cards.SetPinnedAsync(_card.Id, IsPinned).ConfigureAwait(true);
        _card.IsPinned = IsPinned;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleSuspendAsync()
    {
        if (_card is null)
        {
            return;
        }

        IsSuspended = !IsSuspended;
        await _cards.SetSuspendedAsync(_card.Id, IsSuspended).ConfigureAwait(true);
        _card.IsSuspended = IsSuspended;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>В корзину. Это не потеря: карточка возвращается оттуда без изменений (§3.5).</summary>
    [RelayCommand]
    private async Task MoveToTrashAsync()
    {
        if (_card is null)
        {
            return;
        }

        await FlushAsync().ConfigureAwait(true);
        await _cards.MoveToTrashAsync([_card.Id]).ConfigureAwait(true);
        await ClearAsync().ConfigureAwait(true);
        Removed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (_card is null)
        {
            return;
        }

        await _cards.RestoreAsync([_card.Id]).ConfigureAwait(true);
        await ClearAsync().ConfigureAwait(true);
        Removed?.Invoke(this, EventArgs.Empty);
    }

    private async Task DelayThenLoadAsync(Guid cardId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(LoadDelay, ct).ConfigureAwait(true);
            await LoadAsync(cardId, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Выбор уехал дальше — эту карточку показывать уже незачем.
        }
    }

    private async Task LoadAsync(Guid cardId, CancellationToken ct)
    {
        await FlushAsync().ConfigureAwait(true);
        await EnsureListsAsync().ConfigureAwait(true);

        var card = await _cards.GetByIdAsync(cardId, ct).ConfigureAwait(true);
        if (card is null || ct.IsCancellationRequested)
        {
            if (card is null)
            {
                await ClearAsync().ConfigureAwait(true);
            }

            return;
        }

        var tagMap = await _tags.GetForCardsAsync([card.Id], ct).ConfigureAwait(true);
        if (ct.IsCancellationRequested)
        {
            return;
        }

        _loading = true;
        try
        {
            _card = card;
            Front = card.Front;
            Back = card.Back;
            Hint = card.Hint ?? string.Empty;
            Source = card.Source ?? string.Empty;
            IsPinned = card.IsPinned;
            IsSuspended = card.IsSuspended;
            IsDeleted = card.DeletedAt is not null;
            HasHint = Hint.Length > 0;
            HasSource = Source.Length > 0;

            SelectedSubject = Subjects.FirstOrDefault(x => x?.Id == card.SubjectId);
            SelectedDeck = Decks.FirstOrDefault(x => x?.Id == card.DeckId);
            SelectedKind = CardChoices.Kinds.FirstOrDefault(x => x.Value == card.Kind);
            SelectedDifficulty = CardChoices.Difficulties.FirstOrDefault(x => x.Value == card.Difficulty);

            Tags.Clear();
            foreach (var tag in tagMap.GetValueOrDefault(card.Id, []))
            {
                Tags.Add(tag.DisplayName);
            }

            BackDocument = CardWikiLinkRenderer.Render(_markdown, card.Back, OnWikiLinkClicked);
            ReviewText = DescribeReview(card);
            MetaText = DescribeMeta(card);
            StatusText = string.Empty;
            HasCard = true;
            IsEditing = false;
            _dirty = false;
            _tagsDirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ClearAsync()
    {
        await FlushAsync().ConfigureAwait(true);

        _loading = true;
        try
        {
            _card = null;
            HasCard = false;
            IsEditing = false;
            IsDeleted = false;
            Front = string.Empty;
            Back = string.Empty;
            Hint = string.Empty;
            Source = string.Empty;
            HasHint = false;
            HasSource = false;
            Tags.Clear();
            BackDocument = null;
            ReviewText = string.Empty;
            MetaText = string.Empty;
            StatusText = string.Empty;
            _dirty = false;
            _tagsDirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task EnsureListsAsync()
    {
        if (_listsLoaded)
        {
            return;
        }

        var subjects = await _subjects.GetAllAsync().ConfigureAwait(true);
        var decks = await _decks.GetAllAsync().ConfigureAwait(true);
        var tags = await _tags.GetAllAsync().ConfigureAwait(true);

        Subjects.Clear();
        Subjects.Add(null);
        foreach (var subject in subjects)
        {
            Subjects.Add(subject);
        }

        Decks.Clear();
        Decks.Add(null);
        foreach (var deck in decks)
        {
            Decks.Add(deck);
        }

        TagSuggestions.Clear();
        foreach (var tag in tags)
        {
            TagSuggestions.Add(tag.DisplayName);
        }

        _listsLoaded = true;
    }

    private void OnTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_loading || _card is null)
        {
            return;
        }

        _tagsDirty = true;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_loading || _card is null)
        {
            return;
        }

        _dirty = true;
        StatusText = "Сохранение…";

        CancelPendingSave();
        var cts = new CancellationTokenSource();
        _saveCts = cts;

        _ = DelayThenSaveAsync(cts.Token);
    }

    private async Task DelayThenSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SaveDelay, ct).ConfigureAwait(true);
            await SaveContentAsync().ConfigureAwait(true);

            if (_tagsDirty)
            {
                await SaveTagsAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Пользователь продолжил печатать — запишет следующая попытка.
        }
    }

    private async Task SaveContentAsync()
    {
        if (_card is not { } card)
        {
            return;
        }

        var result = await _cards.UpdateContentAsync(
            card.Id,
            Front,
            Back,
            Hint,
            Source,
            SelectedKind?.Value ?? card.Kind,
            SelectedDifficulty?.Value ?? card.Difficulty,
            SelectedSubject?.Id,
            SelectedDeck?.Id).ConfigureAwait(true);

        if (result.IsFailure)
        {
            StatusText = result.Error.Message;
            return;
        }

        _dirty = false;
        HasHint = Hint.Length > 0;
        HasSource = Source.Length > 0;
        StatusText = $"Сохранено {DateTime.Now.ToString("HH:mm", Ru)}";

        // Локальная копия должна остаться правдой: по ней панель решает, что показывать.
        card.Front = Front;
        card.Back = Back;
        card.SubjectId = SelectedSubject?.Id;
        card.DeckId = SelectedDeck?.Id;

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveTagsAsync()
    {
        if (_card is not { } card)
        {
            return;
        }

        var result = await _cards.SetTagsAsync(card.Id, Tags.ToList()).ConfigureAwait(true);
        _tagsDirty = false;

        if (result.IsFailure)
        {
            StatusText = result.Error.Message;
            return;
        }

        await RefreshListsAsync().ConfigureAwait(true);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void CancelPendingSave()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = null;
    }

    private static string DescribeReview(Card card) => card.DueAt switch
    {
        null => "Новая карточка — в тренировках ещё не была",
        var due when due <= DateTimeOffset.Now => "Пора повторить",
        var due => $"Повторение {due.Value.LocalDateTime.ToString("d MMMM", Ru)}"
            + (card.Repetitions > 0 ? $" · повторов: {card.Repetitions}" : string.Empty),
    };

    private static string DescribeMeta(Card card) =>
        $"Создана {card.CreatedAt.LocalDateTime.ToString("dd.MM.yyyy", Ru)}"
        + $" · изменена {card.UpdatedAt.LocalDateTime.ToString("dd.MM HH:mm", Ru)}"
        + (card.Lapses > 0 ? $" · забывали {card.Lapses} раз" : string.Empty);

    /// <summary>
    /// Клик по wiki-ссылке <c>[[…]]</c> (new_addons.md §7): точное совпадение — сразу переход;
    /// несколько похожих — подсказка вместо угадывания; ни одной — честное «не найдена».
    /// </summary>
    private void OnWikiLinkClicked(string target) => _ = ResolveAndNavigateAsync(target);

    private async Task ResolveAndNavigateAsync(string target)
    {
        var resolved = await _cards.ResolveWikiLinkAsync(target).ConfigureAwait(true);

        if (resolved.ExactCardId is { } id)
        {
            _navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(CardId: id));
            return;
        }

        if (resolved.Candidates.Count == 1)
        {
            _navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(CardId: resolved.Candidates[0].Id));
            return;
        }

        if (resolved.Candidates.Count > 1)
        {
            var names = string.Join(", ", resolved.Candidates.Take(3).Select(c => $"«{c.Front}»"));
            _toasts.Show("Есть похожие карточки", $"Точного совпадения нет: {names}. Откройте нужную из Картотеки.");
            return;
        }

        _toasts.Show("Карточка не найдена", $"«{target}» — такой карточки нет.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Tags.CollectionChanged -= OnTagsChanged;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        CancelPendingSave();
    }
}
