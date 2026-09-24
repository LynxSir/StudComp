using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.ReportForge.Services;
using StudComp.Services;
using StudComp.ViewModels.Organizer.Hub;
using StudComp.ViewModels.ReportForge;

namespace StudComp.ViewModels.Organizer;

/// <summary>
/// Хаб предмета (new_addons.md §5) — «всё про этот предмет» в одном месте: часы за семестр, файлы,
/// заметки, расписание и оценки. Открывается кликом по паре в расписании, карточке предмета,
/// строке Дашборда или чипу активного предмета в титул-баре.
/// </summary>
/// <remarks>
/// Отдельного сервиса-агрегатора у Хаба нет: каждая вкладка берёт данные из уже существующих
/// сервисов модуля, а «Оценки» целиком переиспользуют <see cref="GradeBookViewModel"/> с
/// выключенным пикером предмета — дублировать 350 строк ради одного экрана незачем.
/// </remarks>
public sealed partial class SubjectHubViewModel : ObservableObject, INavigationAware
{
    private readonly ISubjectService _subjects;
    private readonly ISemesterService _semesters;
    private readonly IScheduleService _schedule;
    private readonly IStudyWorkspace _workspace;
    private readonly IReportTemplateService _reportTemplates;
    private readonly IShellLauncher _shell;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;

    private Guid _subjectId;

    public SubjectHubViewModel(
        ISubjectService subjects,
        ISemesterService semesters,
        IScheduleService schedule,
        IStudyWorkspace workspace,
        IReportTemplateService reportTemplates,
        IShellLauncher shell,
        INavigationService navigation,
        IDialogService dialogs,
        IToastService toasts,
        HubOverviewViewModel overview,
        HubFilesViewModel files,
        HubScheduleViewModel scheduleTab,
        HubNotesViewModel notes,
        GradeBookViewModel grades,
        HubCardsViewModel cardsTab,
        HubDeadlinesViewModel deadlinesTab)
    {
        _subjects = subjects;
        _semesters = semesters;
        _schedule = schedule;
        _workspace = workspace;
        _reportTemplates = reportTemplates;
        _shell = shell;
        _navigation = navigation;
        _dialogs = dialogs;
        _toasts = toasts;

        Overview = overview;
        Files = files;
        Schedule = scheduleTab;
        Notes = notes;
        Grades = grades;
        Cards = cardsTab;
        Deadlines = deadlinesTab;

        // «Заметка к файлу» из контекст-меню сразу открывает её в редакторе на соседней вкладке.
        Files.NoteCreated += async (_, noteId) =>
        {
            await Notes.LoadAsync(_subjectId, noteId);
            SelectedTabIndex = SubjectHubTabs.ToIndex(SubjectHubTab.Notes);
        };

        // «Карточка к файлу» (new_addons.md §7.4) — так же переключает на соседнюю вкладку.
        Files.CardCreated += async (_, _) =>
        {
            await Cards.LoadAsync(_subjectId);
            SelectedTabIndex = SubjectHubTabs.ToIndex(SubjectHubTab.Cards);
        };
    }

    public HubOverviewViewModel Overview { get; }

    public HubFilesViewModel Files { get; }

    public HubScheduleViewModel Schedule { get; }

    public HubNotesViewModel Notes { get; }

    public GradeBookViewModel Grades { get; }

    public HubCardsViewModel Cards { get; }

    public HubDeadlinesViewModel Deadlines { get; }

    [ObservableProperty]
    private string _subjectName = string.Empty;

    [ObservableProperty]
    private string _subjectCode = string.Empty;

    [ObservableProperty]
    private Brush _accentBrush = Brushes.Transparent;

    /// <summary>Строка шапки: семестр, преподаватели, аудитории.</summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Есть ли куда вернуться — кнопка «Назад» в шапке Хаба.</summary>
    public bool CanGoBack => _navigation.CanGoBack;

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not SubjectHubParameter hub)
        {
            return;
        }

        _subjectId = hub.SubjectId;
        SelectedTabIndex = SubjectHubTabs.ToIndex(hub.Tab);
        _ = LoadAsync(hub);
    }

    public void OnNavigatedFrom()
    {
        // Уходя со страницы, дописываем несохранённое в редакторе заметки: автосохранение работает
        // с задержкой, и последняя строка иначе потерялась бы вместе с экраном.
        _ = Notes.FlushAsync();
    }

    private async Task LoadAsync(SubjectHubParameter parameter)
    {
        var subject = await _subjects.GetByIdAsync(_subjectId).ConfigureAwait(true);
        if (subject is null)
        {
            _toasts.Show("Предмет не найден", "Возможно, он был удалён.", ToastKind.Warning);
            _navigation.GoBack();
            return;
        }

        SubjectName = subject.Name;
        SubjectCode = subject.Code;
        AccentBrush = SubjectColor.BrushFor(subject.ColorHex);
        Subtitle = await BuildSubtitleAsync(subject).ConfigureAwait(true);

        await Overview.LoadAsync(_subjectId).ConfigureAwait(true);
        await Files.LoadAsync(_subjectId).ConfigureAwait(true);
        await Schedule.LoadAsync(_subjectId, parameter.HighlightScheduleEntryId).ConfigureAwait(true);
        await Notes.LoadAsync(_subjectId, parameter.NoteId).ConfigureAwait(true);
        await Grades.LockToSubjectAsync(_subjectId).ConfigureAwait(true);
        await Cards.LoadAsync(_subjectId).ConfigureAwait(true);
        await Deadlines.LoadAsync(_subjectId).ConfigureAwait(true);

        IsLoaded = true;
        OnPropertyChanged(nameof(CanGoBack));
    }

    private async Task<string> BuildSubtitleAsync(Subject subject)
    {
        var parts = new List<string> { subject.Assessment.DisplayName() };

        if (subject.SemesterId is { } semesterId
            && await _semesters.GetByIdAsync(semesterId).ConfigureAwait(true) is { } semester)
        {
            parts.Add(semester.Name);
        }

        var entries = await _schedule.GetBySubjectAsync(_subjectId).ConfigureAwait(true);

        var teachers = entries
            .Select(x => x.Teacher)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (teachers.Length > 0)
        {
            parts.Add(string.Join(", ", teachers));
        }

        var rooms = entries
            .Select(x => x.Room)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (rooms.Length > 0)
        {
            parts.Add("ауд. " + string.Join(", ", rooms));
        }

        return string.Join(" · ", parts);
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    /// <summary>Открыть папку предмета в проводнике.</summary>
    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var subject = await _subjects.GetByIdAsync(_subjectId);
        if (subject is null)
        {
            return;
        }

        var directory = _workspace.GetSubjectDirectory(subject);
        if (string.IsNullOrWhiteSpace(directory))
        {
            _toasts.Show(
                "Учебная папка не выбрана",
                "Задайте её в настройках — тогда у предмета появится своя папка.",
                ToastKind.Warning);
            return;
        }

        // Папки может ещё не быть — предмет мог появиться до выбора учебной папки.
        _workspace.EnsureSubjectScaffold(subject);
        _shell.RevealInExplorer(directory);
    }

    /// <summary>Точка входа «Сгенерировать отчёт по предмету» (new_addons.md §6).</summary>
    [RelayCommand]
    private void GenerateReport() =>
        _navigation.NavigateTo<ReportForgePageViewModel>(new ReportForgeParameter(_subjectId));

    [RelayCommand]
    private async Task EditSubjectAsync()
    {
        var subject = await _subjects.GetByIdAsync(_subjectId);
        if (subject is null)
        {
            return;
        }

        var templates = await _reportTemplates.GetAllAsync();
        var semesterList = await _semesters.GetAllAsync();
        var editor = new SubjectEditorViewModel(subject, templates, semesterList, defaultSemesterId: null, _workspace, _dialogs);

        if (!await _dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            return;
        }

        var result = await _subjects.UpdateAsync(editor.ToModel());
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось сохранить предмет", result.Error.Message, ToastKind.Error);
            return;
        }

        await LoadAsync(new SubjectHubParameter(_subjectId));
    }
}
