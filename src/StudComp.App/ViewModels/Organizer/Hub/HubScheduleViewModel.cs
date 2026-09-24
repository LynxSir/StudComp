using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels;

namespace StudComp.ViewModels.Organizer.Hub;

/// <summary>Строка расписания предмета в Хабе: одна пара недели.</summary>
public sealed class HubScheduleRowViewModel(ScheduleEntry entry, bool isHighlighted)
{
    public ScheduleEntry Entry { get; } = entry;

    /// <summary>Пара, с которой пришли в Хаб, — подсвечивается (new_addons.md §5).</summary>
    public bool IsHighlighted { get; } = isHighlighted;

    public string DayText { get; } = OrganizerChoices.DayName(entry.DayOfWeek);

    public string TimeText { get; } =
        entry.StartTime.ToString("HH\\:mm", CultureInfo.InvariantCulture)
        + "–" + entry.EndTime.ToString("HH\\:mm", CultureInfo.InvariantCulture);

    public string TypeText { get; } = OrganizerChoices.ScheduleTypeName(entry.Type);

    public string ParityText { get; } = entry.WeekParity switch
    {
        WeekParity.Odd => "числитель",
        WeekParity.Even => "знаменатель",
        _ => "каждую неделю",
    };

    public string Room => Entry.Room;

    public string Teacher => Entry.Teacher;
}

/// <summary>
/// Вкладка «Расписание» Хаба: пары этого предмета на неделе с инлайн-правкой (new_addons.md §5).
/// </summary>
public sealed partial class HubScheduleViewModel(
    IScheduleService schedule,
    ISubjectService subjects,
    IDialogService dialogs,
    IToastService toasts,
    INotificationScheduler scheduler) : ObservableObject
{
    private Guid _subjectId;
    private Guid? _highlightEntryId;

    public ObservableCollection<HubScheduleRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    public bool HasItems => Items.Count > 0;

    public async Task LoadAsync(Guid subjectId, Guid? highlightEntryId = null)
    {
        _subjectId = subjectId;
        _highlightEntryId = highlightEntryId ?? _highlightEntryId;
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_subjectId == Guid.Empty)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var entries = await schedule.GetBySubjectAsync(_subjectId).ConfigureAwait(true);

            Items.Clear();
            foreach (var entry in entries
                .OrderBy(x => ((int)x.DayOfWeek + 6) % 7)
                .ThenBy(x => x.StartTime))
            {
                Items.Add(new HubScheduleRowViewModel(entry, entry.Id == _highlightEntryId));
            }

            OnPropertyChanged(nameof(HasItems));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var all = await subjects.GetAllAsync();
        var editor = new ScheduleEntryEditorViewModel(all, existing: null, presetSubjectId: _subjectId);

        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            await ApplyAsync(
                async () => (await schedule.CreateAsync(editor.ToModel())).WithoutValue(),
                "Не удалось добавить пару");
        }
    }

    [RelayCommand]
    private async Task EditAsync(HubScheduleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var all = await subjects.GetAllAsync();
        var editor = new ScheduleEntryEditorViewModel(all, row.Entry);

        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            await ApplyAsync(() => schedule.UpdateAsync(editor.ToModel()), "Не удалось сохранить пару");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(HubScheduleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "Удалить пару?", $"{row.DayText}, {row.TimeText} — пара будет удалена из расписания.");
        if (!confirmed)
        {
            return;
        }

        await ApplyAsync(() => schedule.DeleteAsync(row.Entry.Id), "Не удалось удалить пару");
    }

    private async Task ApplyAsync(Func<Task<Core.Common.Result>> action, string errorTitle)
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
