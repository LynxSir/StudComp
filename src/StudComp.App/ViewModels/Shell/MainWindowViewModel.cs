using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using StudComp.Modules.Archivist.Services;
using StudComp.Core.Abstractions.Cards;
using StudComp.Modules.Cards.Services;
using StudComp.Services;
using StudComp.ViewModels.Archivist;
using StudComp.ViewModels.Cards;
using StudComp.ViewModels.Organizer;
using StudComp.ViewModels.ReportForge;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Shell;

/// <summary>
/// ViewModel главного окна: иконочный сайдбар на 4 раздела + текущая страница через
/// <see cref="INavigationService"/>, модалка настроек, чип активного предмета и индикатор активности
/// архивариуса (new_addons.md §1.3–§1.5, §1.8).
/// </summary>
public sealed partial class MainWindowViewModel
    : ObservableObject,
      IRecipient<OpenSettingsMessage>,
      IRecipient<CardsChangedMessage>,
      IRecipient<OpenReviewRequestedMessage>
{
    private readonly IActiveSubjectProvider _activeSubject;
    private readonly IUnsortedFileService _unsortedFiles;
    private readonly ICardService _cards;

    /// <summary>Пункт «Картотека» — на нём висит счётчик карточек к повторению.</summary>
    private readonly NavigationItem _cardsItem;

    // Гард от рекурсии: сайдбар и ведёт навигацию, и следует за ней.
    private bool _syncingSelection;

    public MainWindowViewModel(
        INavigationService navigation,
        ToastHostViewModel toastHost,
        SettingsShellViewModel settings,
        IActiveSubjectProvider activeSubject,
        IUnsortedFileService unsortedFiles,
        IFileWatcherService fileWatcher,
        IMotionService motion,
        IMessenger messenger,
        ICardService cards,
        CardPaletteViewModel palette)
    {
        Navigation = navigation;
        ToastHost = toastHost;
        Settings = settings;
        Motion = motion;
        Palette = palette;
        _activeSubject = activeSubject;
        _unsortedFiles = unsortedFiles;
        _cards = cards;

        _cardsItem = new NavigationItem("Картотека", SymbolRegular.Layer24, typeof(CardsPageViewModel));

        NavigationItems =
        [
            new NavigationItem("Дашборд", SymbolRegular.Home24, typeof(DashboardPageViewModel)),
            new NavigationItem("Органайзер", SymbolRegular.CalendarLtr24, typeof(OrganizerPageViewModel)),
            new NavigationItem("Архивариус", SymbolRegular.FolderArrowRight24, typeof(ArchivistPageViewModel)),
            new NavigationItem("Отчёты", SymbolRegular.DocumentText24, typeof(ReportForgePageViewModel)),
            _cardsItem,
        ];

        SelectedItem = NavigationItems[0];

        _activeSubject.Changed += (_, _) => OnUiThread(UpdateActiveSubject);
        Navigation.Navigated += (_, _) => OnUiThread(SyncSelectionWithNavigation);
        fileWatcher.StateChanged += (_, _) => OnUiThread(() => _ = RefreshUnsortedCountAsync());
        messenger.Register<OpenSettingsMessage>(this);
        messenger.Register<CardsChangedMessage>(this);
        messenger.Register<OpenReviewRequestedMessage>(this);

        UpdateActiveSubject();
        _ = RefreshUnsortedCountAsync();
        ScheduleCardsDueCount();
    }

    /// <summary>Сервис навигации — к нему привязан <c>ContentControl</c> в разметке окна.</summary>
    public INavigationService Navigation { get; }

    /// <summary>Слой всплывающих тостов — к нему привязан <c>ToastHost</c> поверх контента.</summary>
    public ToastHostViewModel ToastHost { get; }

    /// <summary>Модалка настроек — к ней привязан <c>SettingsOverlay</c>.</summary>
    public SettingsShellViewModel Settings { get; }

    /// <summary>Сервис визуальных настроек — к <c>FontScale</c> привязан <c>LayoutTransform</c> окна.</summary>
    public IMotionService Motion { get; }

    /// <summary>Оверлей быстрого поиска по картотеке — к нему привязан <c>CommandPalette</c>.</summary>
    public CardPaletteViewModel Palette { get; }

    /// <summary>Разделы сайдбара (5 иконок). «Настройки» — шестерёнка в титул-баре, не в этом списке.</summary>
    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem? _selectedItem;

    [ObservableProperty]
    private string? _activeSubjectCaption;

    [ObservableProperty]
    private bool _hasActiveSubject;

    [ObservableProperty]
    private int _unsortedCount;

    /// <summary>Полноэкранный режим: сайдбар скрыт, экран отдан сессии.</summary>
    [ObservableProperty]
    private bool _isImmersive;

    public bool HasUnsorted => UnsortedCount > 0;

    partial void OnUnsortedCountChanged(int value) => OnPropertyChanged(nameof(HasUnsorted));

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is not null && !_syncingSelection)
        {
            Navigation.NavigateTo(value.ViewModelType);
        }
    }

    /// <summary>
    /// Подсветка сайдбара следует за навигацией. На страницах вне рельса (Хаб предмета) выделение
    /// снимается: иначе повторный клик по «залипшему» пункту не менял бы <c>SelectedItem</c>,
    /// <c>ListBox</c> не поднимал бы событие — и пользователь застревал бы на Хабе.
    /// </summary>
    private void SyncSelectionWithNavigation()
    {
        OnPropertyChanged(nameof(CanGoBack));

        // Во время сессии на экране не должно быть ничего, кроме карточки (new_addons.md §8.3):
        // рельс уезжает, титул-бар остаётся — без него нет кнопок окна.
        IsImmersive = Navigation.CurrentViewModel is StudySessionViewModel;

        var currentType = Navigation.CurrentViewModel?.GetType();
        var match = currentType is null
            ? null
            : NavigationItems.FirstOrDefault(x => x.ViewModelType == currentType);

        if (ReferenceEquals(match, SelectedItem))
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            SelectedItem = match;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Вернуться на предыдущую страницу — кнопка «Назад» в титул-баре.</summary>
    [RelayCommand]
    private void GoBack() => Navigation.GoBack();

    /// <summary>Есть ли куда возвращаться — по этому свойству видна кнопка «Назад».</summary>
    public bool CanGoBack => Navigation.CanGoBack;

    /// <summary>Открыть модалку настроек, опционально на конкретном разделе.</summary>
    [RelayCommand]
    private void OpenSettings(SettingsSection? section) => Settings.Open(section);

    /// <summary>Перейти в Архивариус → «Неразобранное» (клик по индикатору активности в сайдбаре).</summary>
    [RelayCommand]
    private void OpenUnsorted() => Navigation.NavigateTo<ArchivistPageViewModel>();

    /// <summary>
    /// Клик по бейджу «к повторению» ведёт прямо на вкладку «Повторение» (new_addons.md §2.1), а не
    /// в библиотеку: счётчик обещает очередь — очередь и должна открыться.
    /// </summary>
    [RelayCommand]
    private void OpenReviewQueue() =>
        Navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(Tab: CardsTab.Review));

    /// <summary>
    /// Открыть оверлей поиска по картотеке — <c>Ctrl+K</c> с любого экрана, в том числе поверх
    /// открытой модалки настроек (new_addons.md §4.6).
    /// </summary>
    [RelayCommand]
    private void OpenPalette() => Palette.Open();

    /// <summary>
    /// Отложить пересчёт бейджа до простоя UI. Раздел «Картотека» не имеет права утяжелять холодный
    /// старт (new_addons.md §9), а один <c>COUNT</c> по индексу спокойно подождёт первого кадра.
    /// </summary>
    private void ScheduleCardsDueCount()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _ = RefreshCardsDueCountAsync();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _ = RefreshCardsDueCountAsync());
    }

    /// <summary>
    /// Перечитать счётчик карточек к повторению. Зовётся после первой отрисовки окна и после
    /// каждой правки карточек.
    /// </summary>
    public async Task RefreshCardsDueCountAsync()
    {
        try
        {
            _cardsItem.BadgeCount = await _cards.CountDueAsync().ConfigureAwait(true);
        }
        catch
        {
            // Бейдж не критичен: молча оставляем прежнее значение.
        }
    }

    /// <summary>Открыть Хаб предмета по чипу активного предмета в титул-баре (new_addons.md §1.3).</summary>
    [RelayCommand]
    private void OpenActiveSubjectHub()
    {
        if (_activeSubject.Current is { } info)
        {
            Navigation.NavigateTo<SubjectHubViewModel>(new SubjectHubParameter(info.SubjectId));
        }
    }

    public void Receive(OpenSettingsMessage message) => OnUiThread(() => Settings.Open(message.Section));

    /// <inheritdoc />
    public void Receive(CardsChangedMessage message) =>
        OnUiThread(() => _ = RefreshCardsDueCountAsync());

    /// <summary>
    /// Напоминание о повторении просит открыть очередь. Модуль про навигацию не знает — он шлёт
    /// сообщение, а разбирается с ним App-слой (механизм ARCHITECTURE §11.4).
    /// </summary>
    public void Receive(OpenReviewRequestedMessage message) => OnUiThread(OpenReviewQueue);

    private void UpdateActiveSubject()
    {
        OnPropertyChanged(nameof(CanGoBack));
        var info = _activeSubject.Current;
        HasActiveSubject = info is not null;
        ActiveSubjectCaption = ActiveSubjectPresenter.FormatCaption(info);
    }

    private async Task RefreshUnsortedCountAsync()
    {
        try
        {
            var unsorted = await _unsortedFiles.GetUnsortedAsync().ConfigureAwait(true);
            UnsortedCount = unsorted.Count;
        }
        catch
        {
            // Индикатор — не критичен; молча оставляем прежнее значение.
        }
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action);
        }
    }
}
