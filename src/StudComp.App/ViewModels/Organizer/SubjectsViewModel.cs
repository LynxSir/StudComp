using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.ReportForge.Services;
using StudComp.Services;

namespace StudComp.ViewModels.Organizer;

/// <summary>
/// Вкладка «Предметы»: грид карточек предметов активного семестра с мини-сводкой
/// (ARCHITECTURE §9.1, редизайн — new_addons.md §5). Клик по карточке открывает Хаб предмета.
/// </summary>
public sealed partial class SubjectsViewModel(
    ISubjectService subjects,
    ISemesterService semesters,
    IScheduleService schedule,
    IDeadlineService deadlines,
    IGradeBookService grades,
    IReportTemplateService reportTemplates,
    IStudyWorkspace workspace,
    IDialogService dialogs,
    IToastService toasts,
    INavigationService navigation) : ObservableObject
{
    /// <summary>Карточки предметов для отображения.</summary>
    public ObservableCollection<SubjectCardViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Подпись «Осень 2026 · 3 курс» либо приглашение настроить семестр.</summary>
    [ObservableProperty]
    private string _semesterText = string.Empty;

    public bool HasItems => Items.Count > 0;

    /// <summary>Перечитать список из БД и собрать сводку по каждому предмету.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var semester = await semesters.GetActiveAsync();
            SemesterText = semester is null
                ? "Семестр не настроен"
                : $"{semester.Name} · {semester.CourseNumber} курс";

            var all = await subjects.GetAllAsync();
            var scoped = semester is null
                ? all
                : all.Where(x => x.SemesterId == semester.Id).ToArray() is { Length: > 0 } filtered
                    ? filtered
                    : all;

            // Три запроса на весь экран, а не по три на каждый предмет.
            var entries = await schedule.GetAllAsync();
            var allDeadlines = await deadlines.GetAllAsync();

            var entriesBySubject = entries.ToLookup(x => x.SubjectId);
            var deadlinesBySubject = allDeadlines
                .Where(x => x.Status == DeadlineStatus.Pending)
                .ToLookup(x => x.SubjectId);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var now = DateTimeOffset.Now;

            // Сначала собираем карточки (внутри — await прогноза на предмет), потом заменяем
            // коллекцию одним синхронным блоком: запросы идут на пуле, и наложившиеся обновления
            // иначе давали бы дубли карточек (Phase 13.10).
            var cards = new List<SubjectCardViewModel>();
            foreach (var subject in scoped.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var subjectEntries = entriesBySubject[subject.Id].ToArray();
                var subjectDeadlines = deadlinesBySubject[subject.Id].ToArray();

                var (nextEntry, nextDate) = FindNextClass(subjectEntries, semester, today);
                var hours = TotalHours(subjectEntries, semester, today);
                var forecast = await PredictAsync(subject.Id);

                cards.Add(new SubjectCardViewModel(
                    subject,
                    nextEntry,
                    nextDate,
                    hours,
                    subjectDeadlines.Length,
                    subjectDeadlines.Count(x => x.DueDate < now),
                    forecast));
            }

            Items.Clear();
            foreach (var card in cards)
            {
                Items.Add(card);
            }

            OnPropertyChanged(nameof(HasItems));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Клик по карточке предмета открывает его Хаб (new_addons.md §5).</summary>
    [RelayCommand]
    private void OpenHub(SubjectCardViewModel? card)
    {
        if (card is not null)
        {
            navigation.NavigateTo<SubjectHubViewModel>(new SubjectHubParameter(card.Id));
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var templates = await reportTemplates.GetAllAsync();
        var semesterList = await semesters.GetAllAsync();
        var active = await semesters.GetActiveAsync();

        var editor = new SubjectEditorViewModel(existing: null, templates, semesterList, active?.Id, workspace, dialogs);
        if (!await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            return;
        }

        var subject = editor.ToModel();
        var result = await subjects.CreateAsync(subject);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось создать предмет", result.Error.Message, ToastKind.Error);
            return;
        }

        // Папка предмета и скелет подпапок появляются сразу при создании (new_addons.md §1.1):
        // без этого «Файлы» Хаба открывались бы в несуществующий каталог.
        subject.Id = result.Value;
        EnsureScaffold(subject);

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task EditAsync(SubjectCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var templates = await reportTemplates.GetAllAsync();
        var semesterList = await semesters.GetAllAsync();

        var editor = new SubjectEditorViewModel(card.Subject, templates, semesterList, defaultSemesterId: null, workspace, dialogs);
        if (!await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            return;
        }

        var updated = editor.ToModel();
        var result = await subjects.UpdateAsync(updated);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось сохранить предмет", result.Error.Message, ToastKind.Error);
            return;
        }

        EnsureScaffold(updated);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(SubjectCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "Удалить предмет?",
            $"«{card.Name}» будет удалён вместе с его расписанием, дедлайнами и оценками. "
            + "Файлы и заметки останутся. Это действие необратимо.");
        if (!confirmed)
        {
            return;
        }

        var result = await subjects.DeleteAsync(card.Id);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось удалить предмет", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Создаёт папку предмета и её подпапки. Ошибка файловой системы не должна ронять сохранение
    /// предмета — папку всегда можно создать позже из Хаба.
    /// </summary>
    private void EnsureScaffold(Subject subject)
    {
        try
        {
            workspace.EnsureSubjectScaffold(subject);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            toasts.Show("Папка предмета не создана", ex.Message, ToastKind.Warning);
        }
    }

    private static (ScheduleEntry? Entry, DateOnly? Date) FindNextClass(
        IReadOnlyList<ScheduleEntry> entries, Semester? semester, DateOnly today)
    {
        if (entries.Count == 0)
        {
            return (null, null);
        }

        if (semester?.EndDate is not { } end)
        {
            // Без границ семестра точную дату не назвать — показываем день недели и время.
            var fallback = entries
                .OrderBy(x => ((int)x.DayOfWeek + 6) % 7)
                .ThenBy(x => x.StartTime)
                .First();
            return (fallback, null);
        }

        ScheduleEntry? best = null;
        DateOnly? bestDate = null;

        foreach (var entry in entries)
        {
            var next = AcademicHoursCalculator.NextOccurrence(
                entry, semester.StartDate, end, today, semester.FirstWeekIsOdd);

            if (next is { } date && (bestDate is null || date < bestDate
                || (date == bestDate && entry.StartTime < best!.StartTime)))
            {
                best = entry;
                bestDate = date;
            }
        }

        return best is null ? (entries[0], null) : (best, bestDate);
    }

    private static double? TotalHours(
        IReadOnlyList<ScheduleEntry> entries, Semester? semester, DateOnly today)
    {
        if (entries.Count == 0 || semester?.EndDate is not { } end)
        {
            return null;
        }

        return AcademicHoursCalculator
            .Summarize(entries, semester.StartDate, end, today, semester.FirstWeekIsOdd)
            .TotalHours;
    }

    private async Task<decimal?> PredictAsync(Guid subjectId)
    {
        var forecast = await grades.ForecastAsync(subjectId);
        return forecast.IsSuccess && forecast.Value.Confidence != ConfidenceLevel.Low
            ? forecast.Value.PredictedFinalScore
            : null;
    }
}
