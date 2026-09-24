using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Resources;
using StudComp.Services;
using StudComp.ViewModels.Archivist;
using StudComp.ViewModels.Cards;
using StudComp.ViewModels.Organizer;
using StudComp.ViewModels.ReportForge;

namespace StudComp.ViewModels.Shell;

/// <summary>Вкладки виджета «Активность» (Phase 13.5) — четыре бывших отдельных зоны Дашборда.</summary>
public enum DashboardActivityTab
{
    Files = 0,
    Notes = 1,
    Reports = 2,
    Sorted = 3,
}

/// <summary>
/// Дашборд — стартовый экран, сводка дня (new_addons.md §1.5). Своих данных не хранит: агрегирует через
/// <see cref="IDashboardService"/>, каждый элемент — ярлык внутрь модуля.
/// </summary>
/// <remarks>
/// Загрузка идёт из <see cref="OnNavigatedTo"/>, а не из <c>Loaded</c> страницы (Phase 13.5): смешение
/// этих двух источников уже давало двойную загрузку в Отчётах (Phase 12.4). Заодно
/// <see cref="OnNavigatedTo"/>/<see cref="OnNavigatedFrom"/> — естественная точка подписки/отписки от
/// <see cref="IActiveSubjectProvider.Changed"/>: VM транзитивная (новый экземпляр на каждый заход на
/// страницу), а сам провайдер — синглтон на весь процесс, так что подписка без отписки копилась бы
/// вечно.
/// </remarks>
public sealed partial class DashboardPageViewModel : ObservableObject, INavigationAware, IPersistentPage
{
    private readonly IDashboardService _dashboard;
    private readonly INavigationService _navigation;
    private readonly IShellLauncher _shell;
    private readonly IMessenger _messenger;

    private readonly INoteService _notes;
    private readonly IToastService _toasts;
    private readonly ICardService _cards;
    private readonly ICramPlanService _cram;
    private readonly IActiveSubjectProvider _activeSubjectProvider;

    public DashboardPageViewModel(
        IDashboardService dashboard,
        INavigationService navigation,
        IShellLauncher shell,
        IMessenger messenger,
        INoteService notes,
        IToastService toasts,
        ICardService cards,
        ICramPlanService cram,
        IActiveSubjectProvider activeSubjectProvider)
    {
        _dashboard = dashboard;
        _navigation = navigation;
        _shell = shell;
        _messenger = messenger;
        _notes = notes;
        _toasts = toasts;
        _cards = cards;
        _cram = cram;
        _activeSubjectProvider = activeSubjectProvider;
    }

    /// <summary>Приветствие по времени суток — вычисляется один раз на заход на страницу.</summary>
    public string Greeting { get; } = ComputeGreeting();

    public ObservableCollection<TodayClassRowViewModel> TodayClasses { get; } = [];

    public ObservableCollection<DashboardDeadline> HotDeadlines { get; } = [];

    public ObservableCollection<DashboardFile> RecentFiles { get; } = [];

    public ObservableCollection<DashboardFile> RecentSortedFiles { get; } = [];

    public ObservableCollection<DashboardNote> RecentNotes { get; } = [];

    public ObservableCollection<DashboardFile> RecentReports { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveSubjectStatusText))]
    [NotifyPropertyChangedFor(nameof(PinToggleTooltip))]
    private ActiveSubjectInfo? _activeSubject;

    /// <summary>«Сейчас: X · до HH:mm» и т.п. — тот же текст, что и в чипе титул-бара (Phase 13.5).</summary>
    public string ActiveSubjectStatusText => ActiveSubjectPresenter.FormatCaption(ActiveSubject);

    public string PinToggleTooltip => ActiveSubject?.IsPinned == true ? "Открепить предмет" : "Закрепить предмет";

    [ObservableProperty]
    private bool _hasStudyRoot;

    [ObservableProperty]
    private string _studyRootPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsorted))]
    [NotifyPropertyChangedFor(nameof(UnsortedActionText))]
    private int _unsortedCount;

    [ObservableProperty]
    private int _sortedTodayCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _daySummaryText = string.Empty;

    /// <summary>Активная вкладка виджета «Активность» (Файлы/Заметки/Отчёты/Архивариус).</summary>
    [ObservableProperty]
    private int _selectedActivityTabIndex;

    public bool ShowOnboarding => !HasStudyRoot;

    public bool HasTodayClasses => TodayClasses.Count > 0;

    public bool HasDeadlines => HotDeadlines.Count > 0;

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public bool HasRecentSortedFiles => RecentSortedFiles.Count > 0;

    public bool HasRecentNotes => RecentNotes.Count > 0;

    public bool HasRecentReports => RecentReports.Count > 0;

    public bool HasUnsorted => UnsortedCount > 0;

    /// <summary>Текст кнопки «Разобрать неразобранное» — приглушённая форма при нуле, не исчезновение.</summary>
    public string UnsortedActionText => UnsortedCount > 0
        ? $"Разобрать неразобранное ({UnsortedCount})"
        : "Неразобранного нет";

    /// <summary>Пара, идущая прямо сейчас, — для контекстных действий в геройской карточке.</summary>
    public TodayClassRowViewModel? CurrentClass => TodayClasses.FirstOrDefault(c => c.IsOnNow);

    /// <summary>Ближайшая пара дня — подсказка в пустом состоянии «Активного предмета».</summary>
    public TodayClassRowViewModel? NextClass => TodayClasses.FirstOrDefault(c => c.IsNext);

    // ---- Картотека (new_addons.md §2.3) -----------------------------------------------------

    /// <summary>Сколько карточек ждёт повторения сегодня.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCardsDue))]
    [NotifyPropertyChangedFor(nameof(CardsDueText))]
    [NotifyPropertyChangedFor(nameof(ReviewActionText))]
    private int _cardsDueCount;

    /// <summary>«Карточка дня» — из тех, которые уже забывали.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCardOfTheDay))]
    private DashboardCard? _cardOfTheDay;

    /// <summary>Показан ли оборот «карточки дня». Смысл зоны в том, чтобы сначала вспомнить самому.</summary>
    [ObservableProperty]
    private bool _isCardAnswerVisible;

    /// <summary>
    /// Обратный отсчёт к ближайшим экзаменам (new_addons.md §6.4) — зона «Аврал».
    /// </summary>
    public ObservableCollection<CramRowViewModel> ExamCountdowns { get; } = [];

    public bool HasExamCountdowns => ExamCountdowns.Count > 0;

    /// <summary>Строка ввода «быстрой карточки» — теперь в панели «Быстрые действия» (Phase 13.5).</summary>
    [ObservableProperty]
    private string _quickCardText = string.Empty;

    public bool HasCardsDue => CardsDueCount > 0;

    public bool HasCardOfTheDay => CardOfTheDay is not null;

    public string CardsDueText => CardsDueCount switch
    {
        0 => "На сегодня всё повторено",
        1 => "К повторению: 1 карточка",
        _ => $"К повторению: {CardsDueCount} {RussianPlural.Of(CardsDueCount, "карточка", "карточки", "карточек")}",
    };

    /// <summary>Текст кнопки «Начать повторение» — приглушённая форма при нуле, не исчезновение.</summary>
    public string ReviewActionText => CardsDueCount > 0 ? $"Повторить ({CardsDueCount})" : "Всё повторено";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var snapshot = await _dashboard.GetSnapshotAsync().ConfigureAwait(true);

            HasStudyRoot = snapshot.HasStudyRoot;
            StudyRootPath = snapshot.StudyRootPath;
            ActiveSubject = snapshot.ActiveSubject;
            UnsortedCount = snapshot.UnsortedCount;
            SortedTodayCount = snapshot.SortedTodayCount;
            CardsDueCount = snapshot.CardsDueCount;
            CardOfTheDay = snapshot.CardOfTheDay;
            IsCardAnswerVisible = false;

            ReplaceTodayClasses(snapshot.TodayClasses);
            Replace(HotDeadlines, snapshot.HotDeadlines);
            Replace(RecentFiles, snapshot.RecentFiles);
            Replace(RecentSortedFiles, snapshot.RecentSortedFiles);
            Replace(RecentNotes, snapshot.RecentNotes);
            Replace(RecentReports, snapshot.RecentReports);

            ExamCountdowns.Clear();
            foreach (var countdown in snapshot.ExamCountdowns)
            {
                ExamCountdowns.Add(new CramRowViewModel(countdown));
            }

            OnPropertyChanged(nameof(ShowOnboarding));
            OnPropertyChanged(nameof(HasTodayClasses));
            OnPropertyChanged(nameof(HasDeadlines));
            OnPropertyChanged(nameof(HasRecentFiles));
            OnPropertyChanged(nameof(HasRecentSortedFiles));
            OnPropertyChanged(nameof(HasRecentNotes));
            OnPropertyChanged(nameof(HasRecentReports));
            OnPropertyChanged(nameof(HasUnsorted));
            OnPropertyChanged(nameof(HasCardsDue));
            OnPropertyChanged(nameof(HasCardOfTheDay));
            OnPropertyChanged(nameof(HasExamCountdowns));

            UpdateDaySummary();
            SelectDefaultActivityTab();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <inheritdoc />
    public void OnNavigatedTo(object? parameter)
    {
        _activeSubjectProvider.Changed += OnActiveSubjectProviderChanged;
        _ = LoadAsync();
    }

    /// <inheritdoc />
    public void OnNavigatedFrom() => _activeSubjectProvider.Changed -= OnActiveSubjectProviderChanged;

    /// <summary>
    /// Смена активной пары обновляется без похода в БД: провайдер уже держит актуальное значение в
    /// памяти, а «сейчас/далее» в уже загруженном списке пересчитывается по времени.
    /// </summary>
    private void OnActiveSubjectProviderChanged(object? sender, EventArgs e) => OnUiThread(RefreshActiveSubjectLive);

    private void RefreshActiveSubjectLive()
    {
        ActiveSubject = _activeSubjectProvider.Current;

        // «Сейчас/далее» пересчитывается заново от текущего времени — тот же метод, что и при
        // полной загрузке, чтобы не разъезжались две копии одной и той же арифметики.
        ReplaceTodayClasses(TodayClasses.Select(r => r.Class).ToArray());
    }

    [RelayCommand]
    private void OpenFile(DashboardFile? file)
    {
        if (file is not null)
        {
            _shell.OpenFile(file.Path);
        }
    }

    [RelayCommand]
    private void RevealFile(DashboardFile? file)
    {
        if (file is not null)
        {
            _shell.RevealInExplorer(file.Path);
        }
    }

    [RelayCommand]
    private void OpenStudyFolder()
    {
        if (!string.IsNullOrWhiteSpace(StudyRootPath))
        {
            _shell.RevealInExplorer(StudyRootPath);
        }
    }

    /// <summary>
    /// Идущая сейчас пара предзаполняет предмет отчёта (new_addons.md §6); если пар нет —
    /// обычный переход в «Отчёты» без предмета, как раньше.
    /// </summary>
    [RelayCommand]
    private void GenerateReport() =>
        _navigation.NavigateTo<ReportForgePageViewModel>(new ReportForgeParameter(ActiveSubject?.SubjectId));

    [RelayCommand]
    private void ResolveUnsorted() => _navigation.NavigateTo<ArchivistPageViewModel>();

    [RelayCommand]
    private void OpenActiveSubject()
    {
        if (ActiveSubject is { } info)
        {
            _navigation.NavigateTo<SubjectHubViewModel>(new SubjectHubParameter(info.SubjectId));
        }
    }

    /// <summary>Закрепить/открепить активный предмет (Phase 13.5 — раньше было только в титул-баре).</summary>
    [RelayCommand]
    private void ToggleActiveSubjectPin()
    {
        _activeSubjectProvider.PinSubject(ActiveSubject is { IsPinned: false } info ? info.SubjectId : null);
    }

    /// <summary>Горящий дедлайн ведёт на страницу работы над ним: задание, материалы, ответ.</summary>
    [RelayCommand]
    private void OpenDeadline(DashboardDeadline? item)
    {
        if (item is not null)
        {
            _navigation.NavigateTo<DeadlineWorkViewModel>(new DeadlineWorkParameter(item.Id));
        }
    }

    /// <summary>Строка таймлайна дня ведёт в Хаб предмета, подсвечивая пару, с которой пришли.</summary>
    [RelayCommand]
    private void OpenClass(DashboardClass? item)
    {
        if (item is not null)
        {
            _navigation.NavigateTo<SubjectHubViewModel>(
                new SubjectHubParameter(item.SubjectId, item.ScheduleEntryId, SubjectHubTab.Schedule));
        }
    }

    /// <summary>
    /// «Быстрая заметка»: создаёт свободную заметку для предмета, который идёт сейчас, и сразу
    /// открывает её в редакторе Хаба (new_addons.md §1.9).
    /// </summary>
    [RelayCommand]
    private async Task QuickNoteAsync()
    {
        if (ActiveSubject is not { } info)
        {
            _toasts.Show(
                "Сейчас нет пары",
                "Быстрая заметка привязывается к идущему предмету — откройте нужный предмет вручную.",
                ToastKind.Warning);
            return;
        }

        await CreateAndOpenAsync(new Note
        {
            SubjectId = info.SubjectId,
            Kind = NoteKind.Free,
            Title = "Быстрая заметка",
            ContentMarkdown = string.Empty,
        });
    }

    /// <summary>
    /// «Быстрая карточка»: одна строка — и карточка уже в картотеке, привязанная к идущему предмету.
    /// Оборот дописывается потом в разделе; смысл в том, чтобы мысль не потерялась на паре.
    /// </summary>
    [RelayCommand]
    private async Task QuickCardAsync()
    {
        var front = QuickCardText.Trim();
        if (front.Length == 0)
        {
            return;
        }

        var result = await _cards.CreateAsync(new Card
        {
            SubjectId = ActiveSubject?.SubjectId,
            Kind = CardKind.Term,
            Front = front,
            Back = string.Empty,
        }).ConfigureAwait(true);

        if (result.IsFailure)
        {
            _toasts.Show("Не удалось создать карточку", result.Error.Message, ToastKind.Error);
            return;
        }

        QuickCardText = string.Empty;
        _messenger.Send(new CardsChangedMessage());
        _toasts.Show("Карточка создана", "Оборот можно дописать в разделе «Картотека».");

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Раскрыть оборот «карточки дня» — сначала вспоминаем, потом проверяем себя.</summary>
    [RelayCommand]
    private void RevealCardOfTheDay() => IsCardAnswerVisible = !IsCardAnswerVisible;

    /// <summary>Открыть «карточку дня» в разделе.</summary>
    [RelayCommand]
    private void OpenCardOfTheDay()
    {
        if (CardOfTheDay is { } card)
        {
            _navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(card.Id));
        }
    }

    /// <summary>
    /// Перейти к тому, что пора повторить. До появления тренажёра (12.8) это библиотека с фильтром
    /// «сегодня» — честный список, а не кнопка, которая ничего не делает.
    /// </summary>
    [RelayCommand]
    private void OpenCardsDue() =>
        _navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(Tab: CardsTab.Review));

    /// <summary>«Готовиться» из зоны обратного отсчёта — сессия в режиме аврала.</summary>
    [RelayCommand]
    private async Task PrepareForExamAsync(CramRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var today = await _cram.GetTodayAsync(row.SubjectId).ConfigureAwait(true);

        if (today.IsFailure)
        {
            _toasts.Show("Аврал", today.Error.Message, ToastKind.Warning);
            return;
        }

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Cram,
            StudySessionFilter.Empty with { CardIds = today.Value, Order = StudyOrder.HardestFirst },
            Title: $"Аврал · {row.SubjectName}"));
    }

    /// <summary>«Заметка к паре» из таймлайна дня: привязывается к паре и сегодняшней дате.</summary>
    [RelayCommand]
    private async Task NoteForClassAsync(DashboardClass? item)
    {
        if (item is null)
        {
            return;
        }

        await CreateAndOpenAsync(new Note
        {
            SubjectId = item.SubjectId,
            Kind = NoteKind.Lecture,
            Title = $"{item.SubjectName}, {DateTime.Today:dd.MM}",
            ContentMarkdown = string.Empty,
            ScheduleEntryId = item.ScheduleEntryId,
            ClassDate = DateOnly.FromDateTime(DateTime.Today),
        });
    }

    /// <summary>Открыть заметку из списка последних.</summary>
    [RelayCommand]
    private void OpenNote(DashboardNote? note)
    {
        if (note?.SubjectId is { } subjectId)
        {
            _navigation.NavigateTo<SubjectHubViewModel>(
                new SubjectHubParameter(subjectId, Tab: SubjectHubTab.Notes, NoteId: note.Id));
        }
    }

    private async Task CreateAndOpenAsync(Note note)
    {
        var result = await _notes.CreateAsync(note);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось создать заметку", result.Error.Message, ToastKind.Error);
            return;
        }

        _navigation.NavigateTo<SubjectHubViewModel>(new SubjectHubParameter(
            note.SubjectId!.Value, Tab: SubjectHubTab.Notes, NoteId: result.Value));
    }

    [RelayCommand]
    private void OpenOnboarding() => _messenger.Send(new OpenSettingsMessage(SettingsSection.Workspace));

    /// <summary>Однострочная сводка дня под заголовком — считается один раз после загрузки снапшота.</summary>
    private void UpdateDaySummary()
    {
        if (TodayClasses.Count == 0 && HotDeadlines.Count == 0 && CardsDueCount == 0)
        {
            DaySummaryText = "Свободный день — самое время разобрать «Неразобранное» или заглянуть в Картотеку";
            return;
        }

        var classesWord = RussianPlural.Of(TodayClasses.Count, "пара", "пары", "пар");
        var deadlinesWord = RussianPlural.Of(HotDeadlines.Count, "дедлайн", "дедлайна", "дедлайнов");
        var cardsWord = RussianPlural.Of(CardsDueCount, "карточка", "карточки", "карточек");

        DaySummaryText =
            $"Сегодня {TodayClasses.Count} {classesWord} · {HotDeadlines.Count} {deadlinesWord} · " +
            $"{CardsDueCount} {cardsWord} к повторению";
    }

    /// <summary>Открывается та вкладка «Активности», где самая свежая запись.</summary>
    private void SelectDefaultActivityTab()
    {
        var candidates = new (int Index, DateTimeOffset? When)[]
        {
            (0, RecentFiles.Count > 0 ? RecentFiles[0].When : null),
            (1, RecentNotes.Count > 0 ? RecentNotes[0].UpdatedAt : null),
            (2, RecentReports.Count > 0 ? RecentReports[0].When : null),
            (3, RecentSortedFiles.Count > 0 ? RecentSortedFiles[0].When : null),
        };

        var best = candidates.Where(c => c.When is not null).OrderByDescending(c => c.When).FirstOrDefault();
        SelectedActivityTabIndex = best.When is not null ? best.Index : 0;
    }

    /// <summary>
    /// Пересчитывает «сейчас/далее» от текущего момента и заменяет список — единая точка, которой
    /// пользуются и полная загрузка (<see cref="LoadAsync"/>), и лёгкое живое обновление при смене
    /// активного предмета (<see cref="RefreshActiveSubjectLive"/>).
    /// </summary>
    private void ReplaceTodayClasses(IReadOnlyList<DashboardClass> classes)
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var ordered = classes.OrderBy(c => c.Start).ToArray();
        var rows = new List<TodayClassRowViewModel>(ordered.Length);
        var nextAssigned = false;

        foreach (var c in ordered)
        {
            var isOnNow = c.Start <= now && now < c.End;
            var isNext = !isOnNow && !nextAssigned && c.Start > now;
            if (isNext)
            {
                nextAssigned = true;
            }

            rows.Add(new TodayClassRowViewModel(c with { IsOnNow = isOnNow }, isNext));
        }

        Replace(TodayClasses, rows);
        OnPropertyChanged(nameof(CurrentClass));
        OnPropertyChanged(nameof(NextClass));
    }

    private static string ComputeGreeting() => DateTime.Now.Hour switch
    {
        >= 5 and < 12 => "Доброе утро",
        >= 12 and < 18 => "Добрый день",
        >= 18 and < 23 => "Добрый вечер",
        _ => "Работаем допоздна",
    };

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

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}

/// <summary>
/// Строка «Расписания на сегодня» — плоские проброшенные свойства поверх <see cref="DashboardClass"/>
/// плюс вычисленный <see cref="IsNext"/> (первая пара после текущего момента, которая ещё не идёт),
/// по образцу уже существующего <see cref="CramRowViewModel"/> (Phase 13.5).
/// </summary>
public sealed class TodayClassRowViewModel(DashboardClass @class, bool isNext)
{
    /// <summary>Исходная запись — источник параметра для <c>OpenClassCommand</c>/<c>NoteForClassCommand</c>.</summary>
    public DashboardClass Class { get; } = @class;

    public Guid ScheduleEntryId => Class.ScheduleEntryId;

    public Guid SubjectId => Class.SubjectId;

    public string SubjectName => Class.SubjectName;

    public string ColorHex => Class.ColorHex;

    public TimeOnly Start => Class.Start;

    public TimeOnly End => Class.End;

    public string Room => Class.Room;

    public ScheduleEntryType Type => Class.Type;

    public bool IsOnNow => Class.IsOnNow;

    public bool IsNext { get; } = isNext;
}
