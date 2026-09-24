using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;

namespace StudComp.ViewModels.ReportForge;

/// <summary>
/// Вкладка «История»: последние задачи генерации из <c>ReportJob</c> с быстрым доступом к файлам,
/// фильтром по предмету, «Сгенерировать заново» и удалением записи (new_addons.md §6).
/// </summary>
public sealed partial class ReportHistoryViewModel(
    IReportJobRepository jobs,
    ISubjectService subjects,
    IShellLauncher shell,
    IDialogService dialogs,
    IToastService toasts) : ObservableObject
{
    /// <summary>С запасом для клиентской фильтрации по предмету, не раздувая контракт репозитория.</summary>
    private const int PageSize = 200;

    private List<ReportJobRowViewModel> _allRows = [];

    /// <summary>Устанавливается родительской <c>ReportForgePageViewModel</c> — переключить на «Новый отчёт».</summary>
    public Action<ReportJob>? RequestRegenerate { get; set; }

    public ObservableCollection<ReportJobRowViewModel> Items { get; } = [];

    public ObservableCollection<Subject> SubjectFilterChoices { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private Subject? _selectedSubjectFilter;

    public bool IsEmpty => Items.Count == 0;

    public bool HasActiveFilter => SelectedSubjectFilter is not null;

    partial void OnSelectedSubjectFilterChanged(Subject? value) => ApplyFilter();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var subjectList = await subjects.GetAllAsync();
            var subjectsById = subjectList.ToDictionary(s => s.Id);

            SubjectFilterChoices.Clear();
            foreach (var subject in subjectList)
            {
                SubjectFilterChoices.Add(subject);
            }

            SelectedSubjectFilter = SubjectFilterChoices.FirstOrDefault(s => s.Id == SelectedSubjectFilter?.Id);

            var recent = await jobs.GetRecentAsync(PageSize);

            // Наличие файла на диске проверяется один раз на строку и пачкой в фоне, а не в геттере
            // при каждой отрисовке контейнера — путь может быть сетевым/на отключённом диске, и
            // File.Exists в биндинге держал UI-поток на каждую строку (Phase 13.10).
            var exists = await Task.Run(() => recent
                .Select(job => !string.IsNullOrWhiteSpace(job.OutputPath) && File.Exists(job.OutputPath))
                .ToArray());

            _allRows = recent
                .Select((job, i) => new ReportJobRowViewModel(
                    job,
                    job.SubjectId is { } id && subjectsById.TryGetValue(id, out var s) ? s.Name : null,
                    exists[i]))
                .ToList();

            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ResetFilter() => SelectedSubjectFilter = null;

    [RelayCommand]
    private void Open(ReportJobRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (!shell.OpenFile(row.OutputPath))
        {
            toasts.Show("Файл недоступен", "Возможно, отчёт удалён или перемещён.", ToastKind.Warning);
        }
    }

    [RelayCommand]
    private void Reveal(ReportJobRowViewModel? row)
    {
        if (row is not null && !shell.RevealInExplorer(row.OutputPath))
        {
            toasts.Show("Папка недоступна", "Возможно, отчёт удалён или перемещён.", ToastKind.Warning);
        }
    }

    /// <summary>Переключает на «Новый отчёт» и подставляет предмет/шаблон/путь этого запуска.</summary>
    [RelayCommand]
    private void Regenerate(ReportJobRowViewModel? row)
    {
        if (row is not null)
        {
            RequestRegenerate?.Invoke(row.Job);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(ReportJobRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "Удалить запись?",
            $"«{row.FileName}» пропадёт из истории. Сам файл на диске не тронется.");
        if (!confirmed)
        {
            return;
        }

        await jobs.DeleteAsync(row.Id);
        _allRows.RemoveAll(x => x.Id == row.Id);
        Items.Remove(row);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ApplyFilter()
    {
        IEnumerable<ReportJobRowViewModel> query = _allRows;

        if (SelectedSubjectFilter is { } subject)
        {
            query = query.Where(r => r.Job.SubjectId == subject.Id);
        }

        Items.Clear();
        foreach (var row in query)
        {
            Items.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasActiveFilter));
    }
}

/// <summary>Строка истории отчётов.</summary>
/// <param name="fileExists">Снимок «файл на месте» на момент загрузки списка (проверяется в фоне, не в геттере).</param>
public sealed class ReportJobRowViewModel(ReportJob job, string? subjectName, bool fileExists)
{
    public ReportJob Job => job;

    public Guid Id => job.Id;

    public string OutputPath => job.OutputPath;

    public string SubjectName => subjectName ?? "Без предмета";

    public string FileName => Path.GetFileName(job.OutputPath) is { Length: > 0 } name ? name : job.OutputPath;

    public string FolderName => Path.GetDirectoryName(job.OutputPath) ?? string.Empty;

    public string CreatedText => job.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public string StatusText => job.Status switch
    {
        ReportJobStatus.Rendered => "Готов",
        ReportJobStatus.Failed => "Ошибка",
        _ => "В работе",
    };

    /// <summary>Готовый отчёт подсвечивается золотой рамкой — акцент завершённости (ARCHITECTURE §19.2).</summary>
    public bool IsReady => job.Status == ReportJobStatus.Rendered;

    public bool IsFailed => job.Status == ReportJobStatus.Failed;

    /// <summary>Файл мог быть удалён вручную — тогда кнопки «Открыть» показывать бессмысленно.</summary>
    public bool FileExists => fileExists;

    /// <summary>«Файл отсутствует» — состояние из DoD new_addons.md §6.</summary>
    public bool IsMissing => !FileExists;
}
