using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Resources;
using StudComp.Services;

namespace StudComp.ViewModels.ReportForge;

/// <summary>
/// Раздел «Отчёты» (ARCHITECTURE §10): вкладки «Новый отчёт», «История» и «Профили оформления».
/// Активная вкладка перечитывает данные при показе и переключении (ADR §16.23). Поддерживает
/// навигацию с параметром — точка входа «Сгенерировать отчёт по предмету» (new_addons.md §6).
/// </summary>
public sealed partial class ReportForgePageViewModel : ObservableObject, INavigationAware, IPersistentPage
{
    public ReportForgePageViewModel(
        NewReportViewModel newReport,
        ReportHistoryViewModel history,
        ProfilesViewModel profiles,
        IDialogService dialogs,
        IActiveSubjectProvider activeSubject)
    {
        NewReport = newReport;
        History = history;
        Profiles = profiles;
        _dialogs = dialogs;
        _activeSubject = activeSubject;

        // «Сгенерировать заново» из Истории — переключить на «Новый отчёт» и подставить прошлые данные.
        History.RequestRegenerate = job =>
        {
            SelectedTabIndex = 0;
            _ = NewReport.PrefillFromHistoryAsync(job);
        };
    }

    private readonly IDialogService _dialogs;

    private readonly IActiveSubjectProvider _activeSubject;

    /// <summary>
    /// Предмет по умолчанию подставляется один раз за сессию: страница живёт всю сессию
    /// (<see cref="IPersistentPage"/>), поэтому обычного приватного поля достаточно – и дальше
    /// выбор пользователя уже не перебивается.
    /// </summary>
    private bool _defaultSubjectApplied;

    public NewReportViewModel NewReport { get; }

    public ReportHistoryViewModel History { get; }

    public ProfilesViewModel Profiles { get; }

    [ObservableProperty]
    private int _selectedTabIndex;


    /// <summary>Помощник по разделу: открывается на теме текущей подвкладки.</summary>
    [RelayCommand]
    private Task ShowHelpAsync() => _dialogs.ShowInfoAsync(
        new SectionHelpViewModel(HelpSection.ReportForge, SelectedTabIndex),
        HelpCatalog.TitleOf(HelpSection.ReportForge),

        // Запас по ширине: содержимое ровно в DialogMaxWidth обрезается полями диалога.
        maxWidth: 860);

    partial void OnSelectedTabIndexChanged(int value)
    {
        UiActivity.Mark($"Вкладка Отчёты/{value}");
        _ = RefreshCurrentAsync();
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is ReportForgeParameter { SubjectId: { } subjectId })
        {
            // Пришли «сгенерировать отчёт по предмету» – свой предмет важнее любого умолчания.
            _defaultSubjectApplied = true;
            SelectedTabIndex = 0;
            _ = NewReport.PreselectSubjectAsync(subjectId);
        }
        else
        {
            _ = OpenAsync();
        }
    }

    /// <summary>
    /// Открыть раздел: перечитать активную вкладку и, если это первый заход за сессию, подставить
    /// предмет текущей пары (а если пары нет – ближайшей следующей). Пустой комбобокс при каждом
    /// первом открытии раздела был прямой жалобой владельца.
    /// </summary>
    private async Task OpenAsync()
    {
        await RefreshCurrentAsync();
        await ApplyDefaultSubjectAsync();
    }

    private async Task ApplyDefaultSubjectAsync()
    {
        if (_defaultSubjectApplied)
        {
            return;
        }

        // Провайдер считает активный предмет не сразу после старта приложения – если раздел открыли
        // раньше, просим посчитать сейчас, иначе умолчания просто не будет.
        if (_activeSubject.Current is null)
        {
            await _activeSubject.RefreshAsync();
        }

        if (_activeSubject.Current is not { } active)
        {
            return;
        }

        _defaultSubjectApplied = true;

        // Обе вкладки перечитывают списки сами и на каждом прогоне сбрасывают выбор, поэтому
        // предмет ставится после их обновления, а не до.
        if (NewReport.Subjects.Count == 0)
        {
            await NewReport.RefreshAsync();
        }

        NewReport.SelectedSubject = NewReport.Subjects
            .FirstOrDefault(subject => subject.Id == active.SubjectId) ?? NewReport.SelectedSubject;

        // «История» могла ещё не загружаться — грузить её только ради фильтра незачем, она сама
        // применит пожелание на своём первом обновлении.
        if (History.SubjectFilterChoices.Count == 0)
        {
            History.DefaultSubjectFilterId = active.SubjectId;
        }
        else
        {
            History.SelectedSubjectFilter = History.SubjectFilterChoices
                .FirstOrDefault(subject => subject.Id == active.SubjectId) ?? History.SelectedSubjectFilter;
        }
    }

    public void OnNavigatedFrom()
    {
        // Несохранённого состояния нет — в отличие от заметок, генерация не автосохраняется.
    }

    /// <summary>Перечитать данные активной вкладки.</summary>
    [RelayCommand]
    public Task RefreshCurrentAsync() => SelectedTabIndex switch
    {
        1 => History.RefreshAsync(),
        2 => Profiles.RefreshAsync(),
        _ => NewReport.RefreshAsync(),
    };
}
