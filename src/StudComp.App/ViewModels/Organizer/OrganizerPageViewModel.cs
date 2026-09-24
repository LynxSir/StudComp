using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using StudComp.Core.Domain;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels.Shell;

namespace StudComp.ViewModels.Organizer;

/// <summary>
/// Физический порядок вкладок раздела «Органайзер» (new_addons.md §3.2) — enum вместо голых
/// int-индексов в <see cref="OrganizerPageViewModel.RefreshCurrentAsync"/>, чтобы следующая
/// перестановка вкладок в XAML не разошлась со switch молча, как это случилось с
/// <c>SubjectHubTab</c> (Phase 12.4/12.6).
/// </summary>
public enum OrganizerTab
{
    Schedule = 0,
    Deadlines = 1,
    GradeBook = 2,
    Subjects = 3,
}

/// <summary>
/// Раздел «Органайзер» (ARCHITECTURE §9): предметы, расписание, дедлайны и зачётка активного
/// семестра. Переключатель семестра в шапке позволяет посмотреть и поправить прошлые
/// (new_addons.md §5). Активная вкладка перечитывает данные при показе.
/// </summary>
public sealed partial class OrganizerPageViewModel : ObservableObject, INavigationAware, IPersistentPage
{
    private readonly ISemesterService _semesters;
    private readonly IMessenger _messenger;

    private bool _switching;

    public OrganizerPageViewModel(
        SubjectsViewModel subjects,
        ScheduleViewModel schedule,
        DeadlinesViewModel deadlines,
        GradeBookViewModel gradeBook,
        ISemesterService semesters,
        IMessenger messenger)
    {
        Subjects = subjects;
        Schedule = schedule;
        Deadlines = deadlines;
        GradeBook = gradeBook;
        _semesters = semesters;
        _messenger = messenger;
    }

    public SubjectsViewModel Subjects { get; }

    public ScheduleViewModel Schedule { get; }

    public DeadlinesViewModel Deadlines { get; }

    public GradeBookViewModel GradeBook { get; }

    /// <summary>Семестры для переключателя в шапке, свежие сверху.</summary>
    public ObservableCollection<Semester> Semesters { get; } = [];

    [ObservableProperty]
    private Semester? _selectedSemester;

    [ObservableProperty]
    private bool _hasSemesters;

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        UiActivity.Mark($"Вкладка Органайзер/{value}");
        _ = RefreshCurrentAsync();
    }

    partial void OnSelectedSemesterChanged(Semester? value) => _ = SwitchSemesterAsync(value);

    /// <inheritdoc />
    public void OnNavigatedTo(object? parameter)
    {
        // Страница кешируется и переключается через Visibility, Loaded повторно не стреляет —
        // единственный источник загрузки при показе теперь здесь (Phase 13.10).
        _ = LoadAsync();
    }

    /// <inheritdoc />
    public void OnNavigatedFrom()
    {
    }

    /// <summary>Перечитать шапку и данные активной вкладки. Зовётся при показе страницы.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        await ReloadSemestersAsync();
        await RefreshCurrentAsync();
    }

    /// <summary>Перечитать данные активной вкладки (при загрузке страницы и смене вкладки).</summary>
    [RelayCommand]
    public Task RefreshCurrentAsync() => (OrganizerTab)SelectedTabIndex switch
    {
        OrganizerTab.Schedule => Schedule.RefreshAsync(),
        OrganizerTab.Deadlines => Deadlines.RefreshAsync(),
        OrganizerTab.GradeBook => GradeBook.RefreshAsync(),
        _ => Subjects.RefreshAsync(),
    };

    /// <summary>Открыть настройки семестров — дип-линк из шапки раздела.</summary>
    [RelayCommand]
    private void ManageSemesters() =>
        _messenger.Send(new OpenSettingsMessage(SettingsSection.Semester));

    private async Task ReloadSemestersAsync()
    {
        var all = await _semesters.GetAllAsync();
        var active = await _semesters.GetActiveAsync();

        _switching = true;
        try
        {
            Semesters.Clear();
            foreach (var semester in all)
            {
                Semesters.Add(semester);
            }

            HasSemesters = Semesters.Count > 0;
            SelectedSemester = Semesters.FirstOrDefault(x => x.Id == active?.Id) ?? Semesters.FirstOrDefault();
        }
        finally
        {
            _switching = false;
        }
    }

    /// <summary>
    /// Выбор семестра в шапке делает его активным: вкладки читают именно активный семестр, и
    /// «посмотреть прошлый» без переключения было бы невозможно.
    /// </summary>
    private async Task SwitchSemesterAsync(Semester? semester)
    {
        if (_switching || semester is null || semester.Id == _semesters.Current?.Id)
        {
            return;
        }

        await _semesters.SetActiveAsync(semester.Id);
        await RefreshCurrentAsync();
    }
}
