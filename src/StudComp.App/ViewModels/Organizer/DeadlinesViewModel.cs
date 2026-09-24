using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;

namespace StudComp.ViewModels.Organizer;

/// <summary>Как сгруппирован список дедлайнов.</summary>
public enum DeadlineGrouping
{
    /// <summary>По сроку: просроченные, сегодня, на неделе, позже.</summary>
    ByWeek = 0,

    /// <summary>По предмету.</summary>
    BySubject = 1,
}

/// <summary>Группа дедлайнов с заголовком.</summary>
public sealed record DeadlineGroupViewModel(
    string Title, int Count, IReadOnlyList<DeadlineRowViewModel> Items);

/// <summary>
/// Вкладка «Дедлайны»: список со статусом (в т.ч. производным «просрочен») и CRUD
/// (ARCHITECTURE §9.3). Редизайн 12.3 добавил группировку и календарный вид (new_addons.md §5).
/// </summary>
public sealed partial class DeadlinesViewModel(
    IDeadlineService deadlines,
    ISubjectService subjects,
    IDialogService dialogs,
    IToastService toasts,
    INotificationScheduler scheduler,
    IShellLauncher shell,
    ICramPlanService cram,
    INavigationService navigation,
    IDeadlineWorkService work) : ObservableObject
{
    /// <summary>Плоский список — источник для группировки и календаря.</summary>
    private IReadOnlyList<DeadlineRowViewModel> _all = [];

    public ObservableCollection<DeadlineRowViewModel> Items { get; } = [];

    /// <summary>Сгруппированный список — то, что видит пользователь в списочном режиме.</summary>
    public ObservableCollection<DeadlineGroupViewModel> Groups { get; } = [];

    /// <summary>Календарь месяца; наполняется теми же дедлайнами.</summary>
    public DeadlineCalendarViewModel Calendar { get; } = new();

    [ObservableProperty]
    private bool _showClosed;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Календарный вид вместо списка (new_addons.md §5).</summary>
    [ObservableProperty]
    private bool _isCalendarView;

    [ObservableProperty]
    private DeadlineGrouping _grouping = DeadlineGrouping.ByWeek;

    public bool IsGroupedBySubject => Grouping == DeadlineGrouping.BySubject;

    public bool HasItems => Items.Count > 0;

    partial void OnShowClosedChanged(bool value) => _ = RefreshAsync();

    partial void OnGroupingChanged(DeadlineGrouping value)
    {
        OnPropertyChanged(nameof(IsGroupedBySubject));
        Regroup();
    }

    /// <summary>Переключить группировку из сегментированного контрола шапки.</summary>
    [RelayCommand]
    private void SetGrouping(DeadlineGrouping grouping) => Grouping = grouping;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        // Подписка идемпотентна: сначала снимаем, потом ставим — иначе накопились бы дубликаты.
        Calendar.SelectionChanged -= OnCalendarSelectionChanged;
        Calendar.SelectionChanged += OnCalendarSelectionChanged;

        IsBusy = true;
        try
        {
            var now = DateTimeOffset.Now;
            var subjectList = await subjects.GetAllAsync();
            var byId = subjectList.ToDictionary(s => s.Id);

            var all = await deadlines.GetAllAsync();
            var visible = all
                .Where(d => ShowClosed || d.Status != DeadlineStatus.Done)
                .ToList();
            var counts = await work.GetAttachmentCountsAsync();

            var rows = new List<DeadlineRowViewModel>(visible.Count);
            foreach (var deadline in visible)
            {
                var subject = byId.GetValueOrDefault(deadline.SubjectId);
                // Путь спрашиваем только у тех, где привязка вообще есть — лишних запросов не делаем.
                var linkedPath = deadline.LinkedFileRecordId is null
                    ? null
                    : await deadlines.GetLinkedFilePathAsync(deadline.Id);

                rows.Add(new DeadlineRowViewModel(
                    deadline, subject?.Name ?? "—", subject?.ColorHex, now, linkedPath,
                    counts.GetValueOrDefault(deadline.Id)));
            }

            _all = [.. rows.OrderBy(r => r.IsDone).ThenBy(r => r.DueDate)];

            Calendar.Rebuild(_all);
            Regroup();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Пересобрать видимый список: фильтр календаря + выбранная группировка.</summary>
    private void Regroup()
    {
        var visible = IsCalendarView ? Calendar.Filter(_all) : _all;

        Items.Clear();
        foreach (var row in visible)
        {
            Items.Add(row);
        }

        Groups.Clear();
        foreach (var group in BuildGroups(visible))
        {
            Groups.Add(group);
        }

        OnPropertyChanged(nameof(HasItems));
    }

    private IEnumerable<DeadlineGroupViewModel> BuildGroups(IReadOnlyList<DeadlineRowViewModel> rows)
    {
        if (Grouping == DeadlineGrouping.BySubject)
        {
            return rows
                .GroupBy(x => x.SubjectName)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new DeadlineGroupViewModel(g.Key, g.Count(), [.. g]));
        }

        var now = DateTimeOffset.Now;
        var endOfWeek = now.Date.AddDays(7 - (((int)now.DayOfWeek + 6) % 7));

        return rows
            .GroupBy(x => BucketFor(x, now, endOfWeek))
            .OrderBy(g => g.Key.Order)
            .Select(g => new DeadlineGroupViewModel(g.Key.Title, g.Count(), [.. g]));
    }

    private static (int Order, string Title) BucketFor(
        DeadlineRowViewModel row, DateTimeOffset now, DateTime endOfWeek)
    {
        if (row.IsDone)
        {
            return (4, "Закрытые");
        }

        if (row.IsOverdue)
        {
            return (0, "Просрочено");
        }

        return row.DueDate.LocalDateTime.Date == now.LocalDateTime.Date
            ? (1, "Сегодня")
            : row.DueDate.LocalDateTime < endOfWeek
                ? (2, "На этой неделе")
                : (3, "Позже");
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var subjectList = await subjects.GetAllAsync();
        if (subjectList.Count == 0)
        {
            toasts.Show("Нет предметов", "Сначала добавьте хотя бы один предмет на вкладке «Предметы».", ToastKind.Warning);
            return;
        }

        var editor = new DeadlineEditorViewModel(subjectList, existing: null);
        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            await ApplyAsync(async () => (await deadlines.CreateAsync(editor.ToModel())).WithoutValue(), "Не удалось создать дедлайн");
        }
    }

    [RelayCommand]
    private async Task EditAsync(DeadlineRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var subjectList = await subjects.GetAllAsync();
        var editor = new DeadlineEditorViewModel(subjectList, row.Deadline);
        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            await ApplyAsync(() => deadlines.UpdateAsync(editor.ToModel()), "Не удалось сохранить дедлайн");
        }
    }

    [RelayCommand]
    private async Task ToggleDoneAsync(DeadlineRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var target = row.IsDone ? DeadlineStatus.Pending : DeadlineStatus.Done;
        await ApplyAsync(() => deadlines.SetStatusAsync(row.Deadline.Id, target), "Не удалось изменить статус");
    }

    [RelayCommand]
    private async Task DeleteAsync(DeadlineRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "Удалить дедлайн?",
            $"«{row.Title}» ({row.SubjectName}) будет удалён вместе с текстом задания и ответа. Файлы и .md-копии в папке предмета останутся.");
        if (!confirmed)
        {
            return;
        }

        await ApplyAsync(() => deadlines.DeleteAsync(row.Deadline.Id), "Не удалось удалить дедлайн");
    }

    /// <summary>Страница работы над дедлайном: задание, материалы, ответ.</summary>
    [RelayCommand]
    private void OpenWork(DeadlineRowViewModel? row)
    {
        if (row is not null)
        {
            navigation.NavigateTo<DeadlineWorkViewModel>(new DeadlineWorkParameter(row.Deadline.Id));
        }
    }

    /// <summary>Открыть файл, привязанный архивариусом (ARCHITECTURE §9.3).</summary>
    [RelayCommand]
    private void OpenLinkedFile(DeadlineRowViewModel? row)
    {
        if (row?.LinkedFilePath is not { Length: > 0 } path)
        {
            return;
        }

        if (!shell.OpenFile(path))
        {
            toasts.Show("Не удалось открыть файл", "Возможно, его переместили или удалили.", ToastKind.Warning);
        }
    }

    /// <summary>Снять привязку файла. Сам файл при этом не трогается (ARCHITECTURE §14).</summary>
    [RelayCommand]
    private async Task UnlinkFileAsync(DeadlineRowViewModel? row)
    {
        if (row is null || !row.HasLinkedFile)
        {
            return;
        }

        await ApplyAsync(() => deadlines.UnlinkFileAsync(row.Deadline.Id), "Не удалось отвязать файл");
    }

    /// <summary>
    /// «Готовиться» на карточке экзамена — сессия в режиме аврала по карточкам предмета
    /// (new_addons.md §2.3). Единственная точка, где Органайзер зовёт Картотеку.
    /// </summary>
    [RelayCommand]
    private async Task PrepareForExamAsync(DeadlineRowViewModel? row)
    {
        if (row is null || !row.IsExam)
        {
            return;
        }

        var today = await cram.GetTodayAsync(row.Deadline.SubjectId);

        if (today.IsFailure)
        {
            toasts.Show("Аврал", today.Error.Message, ToastKind.Warning);
            return;
        }

        navigation.NavigateTo<Cards.StudySessionViewModel>(new Cards.StudySessionParameter(
            StudyMode.Cram,
            StudySessionFilter.Empty with { CardIds = today.Value, Order = StudyOrder.HardestFirst },
            Title: $"Аврал · {row.SubjectName}"));
    }

    partial void OnIsCalendarViewChanged(bool value) => Regroup();

    private void OnCalendarSelectionChanged(object? sender, EventArgs e) => Regroup();

    private async Task ApplyAsync(Func<Task<Result>> action, string errorTitle)
    {
        var result = await action();
        if (result.IsFailure)
        {
            toasts.Show(errorTitle, result.Error.Message, ToastKind.Error);
            return;
        }

        scheduler.RequestReschedule();
        await RefreshAsync();
    }
}
