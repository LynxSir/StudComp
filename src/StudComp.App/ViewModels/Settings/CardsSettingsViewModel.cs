using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;
using StudComp.Services;
using StudComp.ViewModels.Cards;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Раздел «Картотека» (new_addons.md §11): повторение, тренировка, напоминания и состояние
/// поискового индекса. Применяется немедленно — кнопки «Сохранить» нет.
/// </summary>
public sealed partial class CardsSettingsViewModel : SettingsSectionViewModelBase
{
    // "hh" в .NET — 12-часовой формат без AM/PM: час 13 печатался как «01» (new_addons.md §8).
    private const string TimeFormat = @"HH\:mm";

    private readonly UserSettingsProvider _settings;
    private readonly ICardSearchRepository _search;
    private readonly IToastService _toasts;
    private readonly ICardImportExportService _importExport;
    private readonly ICardDeckService _cardDecks;
    private readonly IDialogService _dialogs;

    public CardsSettingsViewModel(
        UserSettingsProvider settings,
        ICardSearchRepository search,
        IToastService toasts,
        ICardImportExportService importExport,
        ICardDeckService cardDecks,
        IDialogService dialogs,
        IOptions<CardsOptions> cards,
        IOptions<DataOptions> data)
    {
        _settings = settings;
        _search = search;
        _toasts = toasts;
        _importExport = importExport;
        _cardDecks = cardDecks;
        _dialogs = dialogs;

        var o = cards.Value;
        using (BeginLoad())
        {
            _reviewEnabled = o.ReviewEnabled;
            _newCardsPerDay = o.NewCardsPerDay;
            _reviewsPerDay = o.ReviewsPerDay;
            _maxIntervalDays = o.MaxIntervalDays;
            _selectedQueueOrder = CardChoices.QueueOrders.FirstOrDefault(x => x.Value == o.QueueOrder)
                ?? CardChoices.QueueOrders[0];
            _relearnInSession = o.RelearnFailedInSameSession;
            _relearnMinutes = o.RelearnMinutes;
            _dayRolloverHour = o.DayRolloverHour;

            _selectedCheckMode = CardChoices.CheckModes.First(x => x.Value == o.DefaultCheckMode);
            _reverseByDefault = o.ReverseByDefault;
            _typedAnswerThreshold = o.TypedAnswerThreshold;
            _showNextIntervalOnButtons = o.ShowNextIntervalOnButtons;
            _practiceAffectsScheduling = o.PracticeAffectsScheduling;
            _cramMinShows = o.CramMinShows;

            _reminderEnabled = o.ReminderEnabled;
            _reminderTimeText = o.ReminderTime.ToString(TimeFormat, CultureInfo.InvariantCulture);

            _searchInBody = o.SearchInBody;
            _searchInTrash = o.SearchInTrash;
            _globalHotkeyEnabled = o.GlobalHotkeyEnabled;
            _globalHotkeyGestureText = o.GlobalHotkeyGesture ?? string.Empty;

            _cardTrashRetentionDays = data.Value.CardTrashRetentionDays;
        }
    }

    /// <inheritdoc />
    public override SettingsSection Section => SettingsSection.Cards;

    /// <inheritdoc />
    public override string Title => "Картотека";

    /// <inheritdoc />
    public override SymbolRegular Icon => SymbolRegular.Layer24;

    /// <inheritdoc />
    public override IEnumerable<string> SearchKeywords =>
    [
        "карточки", "картотека", "повторение", "интервальное повторение", "тренировка", "экзамен",
        "аврал", "новых в день", "лимит", "поисковый индекс", "напоминание о повторении",
        "поиск", "корзина", "компактный режим", "шпаргалка", "горячая клавиша", "экспорт", "импорт",
    ];

    public IReadOnlyList<NamedChoice<StudyOrder>> QueueOrders => CardChoices.QueueOrders;

    public IReadOnlyList<NamedChoice<StudyCheckMode>> CheckModes => CardChoices.CheckModes;

    // ---- Повторение ---------------------------------------------------------------------------

    [ObservableProperty]
    private bool _reviewEnabled;

    [ObservableProperty]
    private int _newCardsPerDay;

    [ObservableProperty]
    private int _reviewsPerDay;

    [ObservableProperty]
    private int _maxIntervalDays;

    [ObservableProperty]
    private NamedChoice<StudyOrder> _selectedQueueOrder;

    [ObservableProperty]
    private bool _relearnInSession;

    [ObservableProperty]
    private int _relearnMinutes;

    [ObservableProperty]
    private int _dayRolloverHour;

    // ---- Тренировка ---------------------------------------------------------------------------

    [ObservableProperty]
    private NamedChoice<StudyCheckMode> _selectedCheckMode;

    [ObservableProperty]
    private bool _reverseByDefault;

    [ObservableProperty]
    private double _typedAnswerThreshold;

    [ObservableProperty]
    private bool _showNextIntervalOnButtons;

    [ObservableProperty]
    private bool _practiceAffectsScheduling;

    [ObservableProperty]
    private int _cramMinShows;

    // ---- Напоминания --------------------------------------------------------------------------

    [ObservableProperty]
    private bool _reminderEnabled;

    [ObservableProperty]
    private string _reminderTimeText;

    // ---- Поисковый индекс ---------------------------------------------------------------------

    [ObservableProperty]
    private string _indexStateText = "Проверяем состояние индекса…";

    [ObservableProperty]
    private bool _isRebuilding;

    // ---- Поиск ----------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _searchInBody;

    [ObservableProperty]
    private bool _searchInTrash;

    // ---- Компактный режим -----------------------------------------------------------------------

    [ObservableProperty]
    private bool _globalHotkeyEnabled;

    [ObservableProperty]
    private string _globalHotkeyGestureText;

    // ---- Данные ---------------------------------------------------------------------------------

    [ObservableProperty]
    private int _cardTrashRetentionDays;

    [ObservableProperty]
    private bool _isExportingOrImporting;

    /// <inheritdoc />
    public override async Task OnActivatedAsync()
    {
        var healthy = await _search.IsFullTextAvailableAsync().ConfigureAwait(true);

        IndexStateText = healthy
            ? "Полнотекстовый индекс работает (FTS5)."
            : "Индекс недоступен — поиск идёт в деградированном режиме и заметно медленнее.";
    }

    /// <summary>Перестроить поисковый индекс — лекарство от рассинхронизации после ребилда таблиц.</summary>
    [RelayCommand]
    private async Task RebuildIndexAsync()
    {
        IsRebuilding = true;

        try
        {
            var count = await _search.RebuildAsync().ConfigureAwait(true);
            _toasts.Show("Индекс перестроен", $"Проиндексировано карточек: {count}.", ToastKind.Success);
            await OnActivatedAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _toasts.Show("Не удалось перестроить индекс", ex.Message, ToastKind.Error);
        }
        finally
        {
            IsRebuilding = false;
        }
    }

    partial void OnReviewEnabledChanged(bool value) => Persist(o => o.ReviewEnabled = value);

    partial void OnNewCardsPerDayChanged(int value) => Persist(o => o.NewCardsPerDay = Math.Clamp(value, 0, 999));

    partial void OnReviewsPerDayChanged(int value) => Persist(o => o.ReviewsPerDay = Math.Clamp(value, 1, 9999));

    partial void OnMaxIntervalDaysChanged(int value) => Persist(o => o.MaxIntervalDays = Math.Clamp(value, 1, 3650));

    partial void OnSelectedQueueOrderChanged(NamedChoice<StudyOrder> value) =>
        Persist(o => o.QueueOrder = value.Value);

    partial void OnRelearnInSessionChanged(bool value) => Persist(o => o.RelearnFailedInSameSession = value);

    partial void OnRelearnMinutesChanged(int value) => Persist(o => o.RelearnMinutes = Math.Clamp(value, 1, 600));

    partial void OnDayRolloverHourChanged(int value) => Persist(o => o.DayRolloverHour = Math.Clamp(value, 0, 23));

    partial void OnSelectedCheckModeChanged(NamedChoice<StudyCheckMode> value) =>
        Persist(o => o.DefaultCheckMode = value.Value);

    partial void OnReverseByDefaultChanged(bool value) => Persist(o => o.ReverseByDefault = value);

    partial void OnTypedAnswerThresholdChanged(double value) =>
        Persist(o => o.TypedAnswerThreshold = Math.Clamp(value, 0.3, 1.0));

    partial void OnShowNextIntervalOnButtonsChanged(bool value) =>
        Persist(o => o.ShowNextIntervalOnButtons = value);

    partial void OnPracticeAffectsSchedulingChanged(bool value) =>
        Persist(o => o.PracticeAffectsScheduling = value);

    partial void OnCramMinShowsChanged(int value) => Persist(o => o.CramMinShows = Math.Clamp(value, 1, 10));

    partial void OnReminderEnabledChanged(bool value) => Persist(o => o.ReminderEnabled = value);

    partial void OnReminderTimeTextChanged(string value)
    {
        if (TimeOnly.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            || TimeOnly.TryParse(value, CultureInfo.CurrentCulture, out time))
        {
            Persist(o => o.ReminderTime = time);
        }
    }

    partial void OnSearchInBodyChanged(bool value) => Persist(o => o.SearchInBody = value);

    partial void OnSearchInTrashChanged(bool value) => Persist(o => o.SearchInTrash = value);

    partial void OnGlobalHotkeyEnabledChanged(bool value) => Persist(o => o.GlobalHotkeyEnabled = value);

    partial void OnGlobalHotkeyGestureTextChanged(string value) =>
        Persist(o => o.GlobalHotkeyGesture = string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    partial void OnCardTrashRetentionDaysChanged(int value) =>
        PersistData(o => o.CardTrashRetentionDays = Math.Clamp(value, 1, 3650));

    /// <summary>Экспортировать всю картотеку — та же двухфазная механика, что и в библиотеке.</summary>
    [RelayCommand]
    private async Task ExportAllAsync()
    {
        var path = _dialogs.PickSaveFile("Экспорт всей картотеки", "Экспорт Rubrica (*.json)|*.json", "rubrica-cards.json");
        if (path is null)
        {
            return;
        }

        IsExportingOrImporting = true;
        try
        {
            var result = await _importExport.ExportJsonAsync(path, new CardExportScope.All()).ConfigureAwait(true);
            if (result.IsFailure)
            {
                _toasts.Show("Не удалось экспортировать", result.Error.Message, ToastKind.Error);
                return;
            }

            _toasts.Show("Экспорт завершён", $"Карточек выгружено: {result.Value}.", ToastKind.Success);
        }
        finally
        {
            IsExportingOrImporting = false;
        }
    }

    /// <summary>Импортировать картотеку из JSON — тот же диалог, что в библиотеке.</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var path = _dialogs.PickOpenFile("Импорт карточек из JSON", "Экспорт Rubrica (*.json)|*.json");
        if (path is null)
        {
            return;
        }

        var decks = (await _cardDecks.GetAllAsync().ConfigureAwait(true)).ToList();
        var editor = new CardsImportViewModel(_importExport, path, CardsImportSourceFormat.Json, decks, sniff: null);

        if (!await _dialogs.ShowEditorAsync(editor, "Импорт карточек", "Импортировать").ConfigureAwait(true))
        {
            return;
        }

        if (editor.Plan is not { } plan)
        {
            return;
        }

        var summary = await _importExport.CommitImportAsync(plan).ConfigureAwait(true);
        if (summary.IsFailure)
        {
            _toasts.Show("Не удалось импортировать", summary.Error.Message, ToastKind.Error);
            return;
        }

        _toasts.Show("Импорт завершён", $"Создано: {summary.Value.Created} · обновлено: {summary.Value.Updated}.");
    }

    /// <summary>
    /// Ретеншн корзины хранится в <see cref="DataOptions"/> (секция <c>Rubrica:Data</c>), а не в
    /// <see cref="CardsOptions"/> — тем же ключом, что и у ленты активности/журнала операций.
    /// </summary>
    private void PersistData(Action<DataOptions> mutate)
    {
        if (!IsLoading)
        {
            _settings.Update(DataOptions.SectionName, mutate);
        }
    }

    private void Persist(Action<CardsOptions> mutate)
    {
        if (!IsLoading)
        {
            _settings.Update(CardsOptions.SectionName, mutate);
        }
    }
}
