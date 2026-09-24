using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        ProfilesViewModel profiles)
    {
        NewReport = newReport;
        History = history;
        Profiles = profiles;

        // «Сгенерировать заново» из Истории — переключить на «Новый отчёт» и подставить прошлые данные.
        History.RequestRegenerate = job =>
        {
            SelectedTabIndex = 0;
            _ = NewReport.PrefillFromHistoryAsync(job);
        };
    }

    public NewReportViewModel NewReport { get; }

    public ReportHistoryViewModel History { get; }

    public ProfilesViewModel Profiles { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        UiActivity.Mark($"Вкладка Отчёты/{value}");
        _ = RefreshCurrentAsync();
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is ReportForgeParameter { SubjectId: { } subjectId })
        {
            SelectedTabIndex = 0;
            _ = NewReport.PreselectSubjectAsync(subjectId);
        }
        else
        {
            _ = RefreshCurrentAsync();
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
