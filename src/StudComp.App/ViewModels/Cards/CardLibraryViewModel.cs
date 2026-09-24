using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using StudComp.Controls;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels.Shell;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Вкладка «Библиотека» (new_addons.md §8.2): рельс фильтров, строка поиска с сеткой карточек и
/// панель просмотра с правкой на месте.
/// </summary>
/// <remarks>
/// <para>
/// Главный элемент экрана — строка поиска, всё остальное ей подчинено. Пункты рельса не работают
/// параллельным механизмом: каждый дописывает свой токен в запрос (а повторный клик его убирает),
/// поэтому фильтр и поиск — одно и то же (new_addons.md §13.9).
/// </para>
/// <para>
/// Поиск идёт с дебаунсом и отменой предыдущего запроса — приём отработан в <c>RuleEditorViewModel</c>
/// и <c>FilePreviewViewModel</c>. Ни одного синхронного обращения к БД в UI-потоке здесь нет, а
/// страницы дозаписываются смещением, а не перезапрашиваются целиком.
/// </para>
/// </remarks>
public sealed partial class CardLibraryViewModel : ObservableObject, IDisposable
{
    /// <summary>Дебаунс строки поиска (new_addons.md §4.6).</summary>
    private const int SearchDebounceMs = 150;

    /// <summary>Префиксные индексы начинаются со второй буквы — раньше идти в базу незачем.</summary>
    private const int MinQueryLength = 2;

    /// <summary>Размер страницы. Экран физически не покажет больше (new_addons.md §4.6).</summary>
    private const int PageSize = 50;

    /// <summary>Глубина кэша запросов: возврат по Backspace обязан быть мгновенным.</summary>
    private const int QueryCacheSize = 20;

    /// <summary>Сколько меток показывать в облаке до нажатия «все метки».</summary>
    private const int TopTagCount = 20;

    private readonly ICardService _cards;
    private readonly ICardDeckService _decks;
    private readonly ICardTagService _tags;
    private readonly ISubjectService _subjects;
    private readonly ICardImportExportService _importExport;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly ITrayService _tray;
    private readonly IActiveSubjectProvider _activeSubject;
    private readonly IMessenger _messenger;

    /// <summary>LRU-кэш последних выдач: ключ — запрос с сортировкой, значение — готовые страницы.</summary>
    private readonly Dictionary<string, IReadOnlyList<CardSearchResult>> _cache = [];
    private readonly LinkedList<string> _cacheOrder = new();

    private CancellationTokenSource? _searchCts;
    private IReadOnlyList<Subject> _subjectList = [];
    private IReadOnlyList<CardQueryTerm> _terms = [];
    private int _loadedCount;

    public CardLibraryViewModel(
        ICardService cards,
        ICardDeckService decks,
        ICardTagService tags,
        ISubjectService subjects,
        ICardImportExportService importExport,
        IDialogService dialogs,
        IToastService toasts,
        ITrayService tray,
        IActiveSubjectProvider activeSubject,
        IMessenger messenger,
        CardPreviewViewModel preview)
    {
        _cards = cards;
        _decks = decks;
        _tags = tags;
        _subjects = subjects;
        _importExport = importExport;
        _dialogs = dialogs;
        _toasts = toasts;
        _tray = tray;
        _activeSubject = activeSubject;
        _messenger = messenger;

        Preview = preview;
        Preview.Saved += OnPreviewSaved;
        Preview.Removed += OnPreviewRemoved;

        _selectedSort = CardChoices.Sorts[0];
    }

    /// <summary>Панель просмотра и правки — третья колонка.</summary>
    public CardPreviewViewModel Preview { get; }

    /// <summary>Список выдачи — на него смотрит и сетка, и режим списка.</summary>
    public ObservableCollection<CardRowViewModel> Items { get; } = [];

    public IReadOnlyList<CardFilterPreset> Presets => CardChoices.Presets;

    public IReadOnlyList<NamedChoice<CardSortOrder>> Sorts => CardChoices.Sorts;

    /// <summary>Предметы рельса вместе с их колодами и счётчиками.</summary>
    public ObservableCollection<CardSubjectNodeViewModel> SubjectNodes { get; } = [];

    /// <summary>Умные подборки — отдельной группой: это сохранённые запросы, а не хранилища.</summary>
    public ObservableCollection<CardDeckNodeViewModel> SmartDecks { get; } = [];

    /// <summary>Колоды без предмета — им в дереве предметов места нет.</summary>
    public ObservableCollection<CardDeckNodeViewModel> LooseDecks { get; } = [];

    /// <summary>Облако меток.</summary>
    public ObservableCollection<CardTag> Tags { get; } = [];

    /// <summary>Предметы для панели массовых операций.</summary>
    public ObservableCollection<Subject?> Subjects { get; } = [];

    /// <summary>Колоды для панели массовых операций.</summary>
    public ObservableCollection<CardDeck?> Decks { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private CardRowViewModel? _selectedItem;

    [ObservableProperty]
    private NamedChoice<CardSortOrder> _selectedSort;

    /// <summary>Сетка или список. Сетка — по умолчанию: раздел должен выглядеть картотекой (§8.1).</summary>
    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasItems;

    [ObservableProperty]
    private bool _canShowMore;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Идёт ли просмотр «Корзины» — от этого зависит набор действий над строкой.</summary>
    [ObservableProperty]
    private bool _isTrashView;

    [ObservableProperty]
    private int _trashCount;

    /// <summary>Показывать ли все метки вместо топа.</summary>
    [ObservableProperty]
    private bool _showAllTags;

    /// <summary>Предупреждение о деградированном поиске (new_addons.md §4.3); пусто — всё в порядке.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchWarning))]
    private string _searchWarning = string.Empty;

    // ---- Мультивыбор и массовые операции ----------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    private int _selectedCount;

    [ObservableProperty]
    private Subject? _bulkSubject;

    [ObservableProperty]
    private CardDeck? _bulkDeck;

    [ObservableProperty]
    private string _bulkTag = string.Empty;

    public bool HasSelection => SelectedCount > 1;

    public string SelectionText => $"Выбрано: {SelectedCount}";

    public bool HasSearchWarning => SearchWarning.Length > 0;

    public bool ShowEmptyState => !HasItems && !IsBusy;

    /// <summary>Что предложить в пустом состоянии: создать карточку прямо из набранного запроса.</summary>
    public string EmptyStateHint => string.IsNullOrWhiteSpace(Query)
        ? "Здесь пока пусто. Создайте первую карточку — термин, формулу или вопрос к экзамену."
        : $"Ничего не нашлось. Создать карточку «{Query.Trim()}»?";

    /// <summary>Перечитать раздел целиком: рельс, выдачу и состояние индекса.</summary>
    public async Task RefreshAsync()
    {
        await LoadRailAsync().ConfigureAwait(true);
        await RunSearchAsync(append: false, CancellationToken.None).ConfigureAwait(true);

        if (!await _cards.IsSearchIndexHealthyAsync().ConfigureAwait(true))
        {
            SearchWarning = "Поиск работает в упрощённом режиме: полнотекстовый индекс недоступен.";
        }
    }

    /// <summary>
    /// Показать конкретную карточку — переход из палитры <c>Ctrl+K</c>, Дашборда или Хаба.
    /// Если её нет в текущей выдаче, она встаёт первой строкой: пользователь просил именно её.
    /// </summary>
    public async Task FocusCardAsync(Guid cardId)
    {
        var existing = Items.FirstOrDefault(x => x.Id == cardId);
        if (existing is not null)
        {
            SelectedItem = existing;
            return;
        }

        var card = await _cards.GetByIdAsync(cardId).ConfigureAwait(true);
        if (card is null)
        {
            return;
        }

        var tagMap = await _tags.GetForCardsAsync([card.Id]).ConfigureAwait(true);
        var row = BuildRow(card, string.Empty, tagMap.GetValueOrDefault(card.Id, []));

        Attach(row);
        Items.Insert(0, row);
        HasItems = true;
        SelectedItem = row;
    }

    /// <summary>Записать несохранённое в панели — уход со страницы и закрытие раздела.</summary>
    public Task FlushAsync() => Preview.FlushAsync();

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(EmptyStateHint));
        ScheduleSearch();
    }

    partial void OnSelectedSortChanged(NamedChoice<CardSortOrder> value) =>
        _ = RunSearchAsync(append: false, CancellationToken.None);

    partial void OnSelectedItemChanged(CardRowViewModel? value) => Preview.Show(value?.Id);

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));

        // Пока идёт запрос, догрузка следующей страницы недоступна — иначе прокрутка вниз успела бы
        // попросить её дважды.
        ShowMoreCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowAllTagsChanged(bool value) => _ = LoadTagsAsync();

    // ---- Рельс фильтров ---------------------------------------------------------------------

    /// <summary>
    /// Пункт рельса — это токен запроса (new_addons.md §13.9). Повторный клик его снимает: фильтр,
    /// который нельзя выключить тем же движением, каким включил, раздражает.
    /// </summary>
    [RelayCommand]
    private void ApplyPreset(CardFilterPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        if (preset.Query.Length == 0)
        {
            Query = string.Empty;
            return;
        }

        ToggleToken(preset.Query);
    }

    [RelayCommand]
    private void ApplyTag(CardTag? tag)
    {
        if (tag is not null)
        {
            ToggleToken("#" + tag.Name);
        }
    }

    [RelayCommand]
    private void ApplyDeck(CardDeckNodeViewModel? node)
    {
        if (node is not null)
        {
            ToggleToken(node.Query);
        }
    }

    [RelayCommand]
    private void ApplySubject(CardSubjectNodeViewModel? node)
    {
        if (node is not null)
        {
            ToggleToken(node.Query);
        }
    }

    [RelayCommand]
    private void ClearQuery() => Query = string.Empty;

    [RelayCommand]
    private void ToggleView() => IsGridView = !IsGridView;

    // ---- Выдача -----------------------------------------------------------------------------

    /// <summary>Догрузить следующую страницу — кнопка и прокрутка к концу списка.</summary>
    [RelayCommand(CanExecute = nameof(CanShowMoreCards))]
    private async Task ShowMoreAsync()
    {
        await RunSearchAsync(append: true, CancellationToken.None).ConfigureAwait(true);
    }

    private bool CanShowMoreCards() => CanShowMore && !IsBusy;

    partial void OnCanShowMoreChanged(bool value) => ShowMoreCommand.NotifyCanExecuteChanged();

    /// <summary>Соседняя карточка — стрелки в библиотеке (new_addons.md §8.4).</summary>
    [RelayCommand]
    private void SelectNext() => MoveSelection(1);

    [RelayCommand]
    private void SelectPrevious() => MoveSelection(-1);

    // ---- Действия над карточкой -------------------------------------------------------------

    [RelayCommand]
    private async Task NewCardAsync()
    {
        var editor = await BuildEditorAsync().ConfigureAwait(true);
        if (!await _dialogs.ShowEditorAsync(editor, editor.HeaderText).ConfigureAwait(true))
        {
            return;
        }

        var result = await _cards.CreateAsync(editor.ToModel(), editor.ParseTags()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось создать карточку", result.Error.Message, ToastKind.Error);
            return;
        }

        DropCache();
        await RefreshAsync().ConfigureAwait(true);
        await FocusCardAsync(result.Value).ConfigureAwait(true);
    }

    /// <summary>Править — это перевести панель в режим правки, а не открыть диалог (§8.2).</summary>
    [RelayCommand]
    private void EditCard(CardRowViewModel? row)
    {
        if (row is not null)
        {
            SelectedItem = row;
        }

        Preview.BeginEditCommand.Execute(null);
    }

    /// <summary>
    /// «Удалить» — это переезд в «Корзину», а не потеря: подтверждения не спрашиваем, спрашиваем
    /// только на очистке корзины (new_addons.md §3.5).
    /// </summary>
    [RelayCommand]
    private async Task DeleteCardAsync(CardRowViewModel? row)
    {
        var targets = ResolveTargets(row);
        if (targets.Count == 0)
        {
            return;
        }

        var result = await _cards.MoveToTrashAsync(targets).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось удалить карточку", result.Error.Message, ToastKind.Error);
            return;
        }

        _toasts.Show(
            targets.Count == 1 ? "Карточка в корзине" : $"В корзине карточек: {targets.Count}",
            "Их можно вернуть в разделе «Корзина».");

        DropCache();
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreCardAsync(CardRowViewModel? row)
    {
        var targets = ResolveTargets(row);
        if (targets.Count == 0)
        {
            return;
        }

        await _cards.RestoreAsync(targets).ConfigureAwait(true);
        DropCache();
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        var count = await _cards.CountDeletedAsync().ConfigureAwait(true);
        if (count == 0)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Очистить корзину?",
            $"Карточек в корзине: {count}. Они будут удалены навсегда — это единственное действие "
            + "над карточками, которое нельзя отменить.",
            "Очистить").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        var result = await _cards.EmptyTrashAsync().ConfigureAwait(true);
        _toasts.Show("Корзина очищена", $"Удалено карточек: {result.Value}.");

        DropCache();
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TogglePinAsync(CardRowViewModel? row)
    {
        var target = row ?? SelectedItem;
        if (target is null)
        {
            return;
        }

        await _cards.SetPinnedAsync(target.Id, !target.IsPinned).ConfigureAwait(true);
        DropCache();
        await RunSearchAsync(append: false, CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleSuspendAsync(CardRowViewModel? row)
    {
        var target = row ?? SelectedItem;
        if (target is null)
        {
            return;
        }

        await _cards.SetSuspendedAsync(target.Id, !target.IsSuspended).ConfigureAwait(true);
        DropCache();
        await RunSearchAsync(append: false, CancellationToken.None).ConfigureAwait(true);
    }

    // ---- Массовые операции ------------------------------------------------------------------

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Items)
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Items)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private Task ApplyBulkSubjectAsync() =>
        RunBulkAsync(ids => _cards.AssignSubjectAsync(ids, BulkSubject?.Id), "Предмет изменён");

    [RelayCommand]
    private Task ApplyBulkDeckAsync() =>
        RunBulkAsync(ids => _cards.AssignDeckAsync(ids, BulkDeck?.Id), "Колода изменена");

    [RelayCommand]
    private Task AddBulkTagAsync() =>
        RunBulkAsync(ids => _cards.AddTagsAsync(ids, ParseBulkTags()), "Метка добавлена");

    [RelayCommand]
    private Task RemoveBulkTagAsync() =>
        RunBulkAsync(ids => _cards.RemoveTagsAsync(ids, ParseBulkTags()), "Метка снята");

    [RelayCommand]
    private Task PinSelectedAsync() =>
        RunBulkAsync(ids => _cards.SetPinnedManyAsync(ids, true), "Карточки закреплены");

    [RelayCommand]
    private Task SuspendSelectedAsync() =>
        RunBulkAsync(ids => _cards.SetSuspendedManyAsync(ids, true), "Карточки отложены");

    // ---- Колоды -----------------------------------------------------------------------------

    [RelayCommand]
    private Task NewDeckAsync() => EditDeckInternalAsync(null);

    [RelayCommand]
    private Task EditDeckAsync(CardDeckNodeViewModel? node) =>
        node is null ? Task.CompletedTask : EditDeckInternalAsync(node.Deck);

    [RelayCommand]
    private async Task DeleteDeckAsync(CardDeckNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Удалить колоду?",
            $"Колода «{node.Title}» будет удалена. Карточки останутся — у них просто пропадёт привязка.")
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        var result = await _decks.DeleteAsync(node.Deck.Id).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось удалить колоду", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Сохранить текущий поиск умной подборкой (new_addons.md §13.8): подборка — это колода с
    /// запросом, поэтому она всегда свежая и не расходится с картотекой.
    /// </summary>
    [RelayCommand]
    private async Task SaveSearchAsDeckAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            _toasts.Show("Нечего сохранять", "Наберите запрос — подборка это сохранённый поиск.");
            return;
        }

        var editor = new CardDeckEditorViewModel(null, _subjectList)
        {
            Name = Query.Trim(),
            QueryExpression = Query.Trim(),
        };

        if (!await _dialogs.ShowEditorAsync(editor, "Новая подборка").ConfigureAwait(true))
        {
            return;
        }

        var result = await _decks.CreateAsync(editor.ToModel()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось сохранить подборку", result.Error.Message, ToastKind.Error);
            return;
        }

        await LoadRailAsync().ConfigureAwait(true);
    }

    /// <summary>Открыть компактный режим-шпаргалку (new_addons.md §8.6) — не покидая раздел.</summary>
    [RelayCommand]
    private void OpenCheatSheet() => _tray.RequestCheatSheet();

    // ---- Импорт и экспорт (new_addons.md §7.6) -----------------------------------------------

    [RelayCommand]
    private Task ExportJsonAsync() => ExportAsync(ResolveExportScope(), isCsv: false);

    [RelayCommand]
    private Task ExportCsvAsync() => ExportAsync(ResolveExportScope(), isCsv: true);

    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        var path = _dialogs.PickOpenFile("Импорт карточек из JSON", "Экспорт Rubrica (*.json)|*.json");
        if (path is null)
        {
            return;
        }

        await RunImportAsync(path, CardsImportSourceFormat.Json, sniff: null).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        var path = _dialogs.PickOpenFile(
            "Импорт карточек из CSV/TSV", "Текстовые таблицы (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Все файлы (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        var sniff = await _importExport.SniffCsvAsync(path).ConfigureAwait(true);
        if (sniff.IsFailure)
        {
            _toasts.Show("Не удалось прочитать файл", sniff.Error.Message, ToastKind.Error);
            return;
        }

        await RunImportAsync(path, CardsImportSourceFormat.Csv, sniff.Value).ConfigureAwait(true);
    }

    /// <summary>Выделение больше одной строки — экспортируем его; иначе — результат текущего поиска.</summary>
    private CardExportScope ResolveExportScope()
    {
        var selected = Items.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        return selected.Count > 1
            ? new CardExportScope.Selection(selected)
            : new CardExportScope.SearchResult(Query);
    }

    private async Task ExportAsync(CardExportScope scope, bool isCsv)
    {
        var path = isCsv
            ? _dialogs.PickSaveFile("Экспорт в CSV", "CSV (*.csv)|*.csv", "rubrica-cards.csv")
            : _dialogs.PickSaveFile("Экспорт в JSON", "Экспорт Rubrica (*.json)|*.json", "rubrica-cards.json");

        if (path is null)
        {
            return;
        }

        // По умолчанию — самый совместимый формат: точка с запятой и UTF-8 с BOM, Excel открывает
        // такой файл без плясок с кодировкой. Выбор разделителя/кодировки — забота импорта чужих файлов.
        var result = isCsv
            ? await _importExport.ExportCsvAsync(path, scope, ';', new System.Text.UTF8Encoding(true)).ConfigureAwait(true)
            : await _importExport.ExportJsonAsync(path, scope).ConfigureAwait(true);

        if (result.IsFailure)
        {
            _toasts.Show("Не удалось экспортировать", result.Error.Message, ToastKind.Error);
            return;
        }

        _toasts.Show("Экспорт завершён", $"Карточек выгружено: {result.Value}.");
    }

    private async Task RunImportAsync(string path, CardsImportSourceFormat format, CardCsvSniffResult? sniff)
    {
        var targetDecks = Decks.Where(d => d is not null).Select(d => d!).ToList();
        var editor = new CardsImportViewModel(_importExport, path, format, targetDecks, sniff);

        if (!await _dialogs.ShowEditorAsync(editor, "Импорт карточек", "Импортировать").ConfigureAwait(true))
        {
            return;
        }

        if (editor.Plan is not { } plan)
        {
            return;
        }

        var progress = new Progress<CardImportProgress>();
        var summary = await _importExport.CommitImportAsync(plan, progress).ConfigureAwait(true);
        if (summary.IsFailure)
        {
            _toasts.Show("Не удалось импортировать", summary.Error.Message, ToastKind.Error);
            return;
        }

        var parts = new List<string> { $"создано: {summary.Value.Created}" };
        if (summary.Value.Updated > 0)
        {
            parts.Add($"обновлено: {summary.Value.Updated}");
        }

        if (summary.Value.Skipped > 0)
        {
            parts.Add($"пропущено: {summary.Value.Skipped}");
        }

        _toasts.Show(
            "Импорт завершён",
            string.Join(" · ", parts),
            summary.Value.Skipped > 0 || summary.Value.Warnings.Count > 0 ? ToastKind.Warning : ToastKind.Success);

        DropCache();
        await RefreshAsync().ConfigureAwait(true);
    }

    // ---- Внутреннее -------------------------------------------------------------------------

    private async Task EditDeckInternalAsync(CardDeck? deck)
    {
        var editor = new CardDeckEditorViewModel(deck, _subjectList);
        if (!await _dialogs.ShowEditorAsync(editor, editor.HeaderText).ConfigureAwait(true))
        {
            return;
        }

        var model = editor.ToModel();
        var result = deck is null
            ? (await _decks.CreateAsync(model).ConfigureAwait(true)).WithoutValue()
            : await _decks.UpdateAsync(model).ConfigureAwait(true);

        if (result.IsFailure)
        {
            _toasts.Show("Не удалось сохранить колоду", result.Error.Message, ToastKind.Error);
            return;
        }

        await LoadRailAsync().ConfigureAwait(true);
    }

    private async Task<CardEditorViewModel> BuildEditorAsync()
    {
        var decks = await _decks.GetAllAsync().ConfigureAwait(true);
        var tags = await _tags.GetAllAsync().ConfigureAwait(true);

        return new CardEditorViewModel(
            null,
            _subjectList,
            decks,
            tags,
            _cards,
            _activeSubject.Current?.SubjectId,
            dialogs: _dialogs);
    }

    private async Task LoadRailAsync()
    {
        _subjectList = await _subjects.GetAllAsync().ConfigureAwait(true);
        var decks = await _decks.GetAllAsync().ConfigureAwait(true);
        var cardCounts = await _cards.CountsBySubjectAsync().ConfigureAwait(true);
        var deckCounts = await _cards.CountsByDeckAsync().ConfigureAwait(true);

        SubjectNodes.Clear();
        foreach (var subject in _subjectList)
        {
            var subjectDecks = decks
                .Where(deck => deck.SubjectId == subject.Id)
                .Select(deck => new CardDeckNodeViewModel(deck, deckCounts.GetValueOrDefault(deck.Id)))
                .ToList();

            SubjectNodes.Add(new CardSubjectNodeViewModel(
                subject,
                cardCounts.GetValueOrDefault(subject.Id),
                subjectDecks));
        }

        SmartDecks.Clear();
        LooseDecks.Clear();
        foreach (var deck in decks)
        {
            var node = new CardDeckNodeViewModel(deck, deckCounts.GetValueOrDefault(deck.Id));
            if (node.IsSmart)
            {
                SmartDecks.Add(node);
            }
            else if (deck.SubjectId is null)
            {
                LooseDecks.Add(node);
            }
        }

        Subjects.Clear();
        Subjects.Add(null);
        foreach (var subject in _subjectList)
        {
            Subjects.Add(subject);
        }

        Decks.Clear();
        Decks.Add(null);
        foreach (var deck in decks)
        {
            Decks.Add(deck);
        }

        await LoadTagsAsync().ConfigureAwait(true);
        TrashCount = await _cards.CountDeletedAsync().ConfigureAwait(true);
    }

    private async Task LoadTagsAsync()
    {
        var tags = ShowAllTags
            ? await _tags.GetAllAsync().ConfigureAwait(true)
            : await _tags.GetTopAsync(TopTagCount).ConfigureAwait(true);

        Tags.Clear();
        foreach (var tag in tags)
        {
            Tags.Add(tag);
        }
    }

    private void ScheduleSearch()
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = DebouncedSearchAsync(cts.Token);
    }

    private async Task DebouncedSearchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, ct).ConfigureAwait(true);
            await RunSearchAsync(append: false, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Заменён более свежим вводом.
        }
    }

    private async Task RunSearchAsync(bool append, CancellationToken ct)
    {
        var spec = CardQuery.Parse(Query);
        _terms = spec.Terms;
        IsTrashView = spec.Flags.HasFlag(CardQueryFlags.Trash);

        // На одном символе идти в индекс рано — показываем недавние и закреплённые.
        var effectiveQuery = spec.HasFullTextPart && Query.Trim().Length < MinQueryLength
            ? string.Empty
            : Query;

        var offset = append ? _loadedCount : 0;
        var cacheKey = $"{SelectedSort.Value}|{offset}|{effectiveQuery}";

        try
        {
            IsBusy = true;

            IReadOnlyList<CardSearchResult> found;
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                found = cached;
            }
            else
            {
                found = await _cards
                    .SearchDetailedAsync(
                        effectiveQuery,
                        new CardSearchOptions(
                            Limit: PageSize,
                            Offset: offset,
                            ActiveSubjectId: _activeSubject.Current?.SubjectId,
                            Sort: SelectedSort.Value),
                        ct)
                    .ConfigureAwait(true);

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                Remember(cacheKey, found);
            }

            await FillAsync(found, append, ct).ConfigureAwait(true);

            CanShowMore = found.Count == PageSize;
            StatusText = Describe(_loadedCount, CanShowMore);
        }
        catch (OperationCanceledException)
        {
            // Заменён более свежим запросом.
        }
        catch (Exception)
        {
            StatusText = "Не удалось выполнить поиск.";
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    private async Task FillAsync(IReadOnlyList<CardSearchResult> found, bool append, CancellationToken ct)
    {
        // Метки читаются пачкой на всю страницу: запрос на строку превратил бы прокрутку в N+1
        // (урок вкладки «Неразобранное», Phase 12.2).
        var tagMap = await _tags
            .GetForCardsAsync(found.Select(x => x.Card.Id).ToList(), ct)
            .ConfigureAwait(true);

        if (ct.IsCancellationRequested)
        {
            return;
        }

        if (!append)
        {
            DetachAll();
            Items.Clear();
            _loadedCount = 0;
        }

        foreach (var result in found)
        {
            var row = BuildRow(result.Card, result.Snippet, tagMap.GetValueOrDefault(result.Card.Id, []));
            Attach(row);
            Items.Add(row);
        }

        // Считаем именно выданное запросом: строка, добавленная переходом «показать вот эту»,
        // смещения следующей страницы менять не должна.
        _loadedCount = append ? _loadedCount + found.Count : found.Count;
        HasItems = Items.Count > 0;
        UpdateSelectionCount();
        OnPropertyChanged(nameof(EmptyStateHint));
    }

    private CardRowViewModel BuildRow(Card card, string snippet, IReadOnlyList<CardTag> tags)
    {
        var subject = card.SubjectId is { } id
            ? _subjectList.FirstOrDefault(x => x.Id == id)
            : null;

        return new CardRowViewModel(card, subject?.Name, subject?.ColorHex, tags, snippet, _terms);
    }

    private void Attach(CardRowViewModel row) => row.PropertyChanged += OnRowPropertyChanged;

    private void DetachAll()
    {
        foreach (var row in Items)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardRowViewModel.IsSelected))
        {
            UpdateSelectionCount();
        }
    }

    private void UpdateSelectionCount() => SelectedCount = Items.Count(x => x.IsSelected);

    private void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        var next = Math.Clamp(index + delta, 0, Items.Count - 1);
        SelectedItem = Items[next];
    }

    /// <summary>Кому адресована операция: явной строке, выделенной пачке или текущей карточке.</summary>
    private List<Guid> ResolveTargets(CardRowViewModel? row)
    {
        var selected = Items.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        if (selected.Count > 1)
        {
            return selected;
        }

        var target = row ?? SelectedItem;
        return target is null ? [] : [target.Id];
    }

    private async Task RunBulkAsync(Func<IReadOnlyList<Guid>, Task<Result<int>>> action, string success)
    {
        var ids = Items.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var result = await action(ids).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось выполнить операцию", result.Error.Message, ToastKind.Error);
            return;
        }

        _toasts.Show(success, $"Затронуто карточек: {ids.Count}.");

        DropCache();
        await RefreshAsync().ConfigureAwait(true);
    }

    private IReadOnlyList<string> ParseBulkTags() =>
        BulkTag.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Добавить токен в запрос либо убрать его, если он уже там. Токены разделяются пробелами, а
    /// значения в кавычках остаются целыми — сравнение идёт по тому же разбору, что и поиск.
    /// </summary>
    private void ToggleToken(string token)
    {
        var current = Query.Trim();
        if (current.Length == 0)
        {
            Query = token;
            return;
        }

        var without = RemoveToken(current, token);
        Query = without is null ? $"{current} {token}" : without;
    }

    private static string? RemoveToken(string query, string token)
    {
        var index = query.IndexOf(token, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        // Токен должен стоять отдельным словом, иначе «новые» вырезалось бы из «обновление».
        var endsAt = index + token.Length;
        var startsClean = index == 0 || char.IsWhiteSpace(query[index - 1]);
        var endsClean = endsAt == query.Length || char.IsWhiteSpace(query[endsAt]);

        if (!startsClean || !endsClean)
        {
            return null;
        }

        return (query[..index] + query[endsAt..]).Replace("  ", " ").Trim();
    }

    private void Remember(string key, IReadOnlyList<CardSearchResult> value)
    {
        if (_cache.ContainsKey(key))
        {
            _cacheOrder.Remove(key);
        }
        else if (_cache.Count >= QueryCacheSize)
        {
            var oldest = _cacheOrder.First;
            if (oldest is not null)
            {
                _cache.Remove(oldest.Value);
                _cacheOrder.RemoveFirst();
            }
        }

        _cache[key] = value;
        _cacheOrder.AddLast(key);
    }

    /// <summary>
    /// Любая правка данных делает кэш враньём — он сбрасывается целиком, а не точечно. Заодно это
    /// единственная точка, где стоит просить окно пересчитать бейдж очереди.
    /// </summary>
    private void DropCache()
    {
        _cache.Clear();
        _cacheOrder.Clear();
        _messenger.Send(new CardsChangedMessage());
    }

    private void OnPreviewSaved(object? sender, EventArgs e)
    {
        DropCache();
        _ = RunSearchAsync(append: false, CancellationToken.None);
    }

    private void OnPreviewRemoved(object? sender, EventArgs e)
    {
        DropCache();
        _ = RefreshAsync();
    }

    private static string Describe(int count, bool more) => count switch
    {
        0 => string.Empty,
        1 => "1 карточка",
        _ when count % 10 is >= 2 and <= 4 && count % 100 is < 11 or > 14 => $"{count} карточки{(more ? " и ещё" : string.Empty)}",
        _ => $"{count} карточек{(more ? " и ещё" : string.Empty)}",
    };

    /// <inheritdoc />
    public void Dispose()
    {
        Preview.Saved -= OnPreviewSaved;
        Preview.Removed -= OnPreviewRemoved;
        // FlushAsync при уходе со страницы уже запущен и не зависит от отменяемых токенов панели
        // (он пишет напрямую) — освобождать панель безопасно.
        Preview.Dispose();
        DetachAll();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
