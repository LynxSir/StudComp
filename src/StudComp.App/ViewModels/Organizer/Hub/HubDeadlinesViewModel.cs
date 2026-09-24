using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels.Cards;

namespace StudComp.ViewModels.Organizer.Hub;

/// <summary>
/// Вкладка «Дедлайны» Хаба предмета (new_addons.md §7.2): те же дедлайны и те же действия, что в
/// общем разделе «Органайзер» (<see cref="DeadlinesViewModel"/>), но отфильтрованные одним предметом —
/// без группировки и календаря общей вкладки, которые здесь не нужны.
/// </summary>
public sealed partial class HubDeadlinesViewModel(
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
    private Guid _subjectId;
    private Subject? _subject;

    public ObservableCollection<DeadlineRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _showClosed;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasItems => Items.Count > 0;

    partial void OnShowClosedChanged(bool value) => _ = LoadAsync(_subjectId);

    public async Task LoadAsync(Guid subjectId)
    {
        _subjectId = subjectId;
        IsBusy = true;
        try
        {
            _subject = await subjects.GetByIdAsync(subjectId).ConfigureAwait(true);

            var now = DateTimeOffset.Now;
            var all = await deadlines.GetBySubjectAsync(subjectId).ConfigureAwait(true);
            var visible = all.Where(d => ShowClosed || d.Status != DeadlineStatus.Done).ToList();
            var counts = await work.GetAttachmentCountsAsync().ConfigureAwait(true);

            var rows = new List<DeadlineRowViewModel>(visible.Count);
            foreach (var deadline in visible)
            {
                // Путь спрашиваем только у тех, где привязка вообще есть — лишних запросов не делаем.
                var linkedPath = deadline.LinkedFileRecordId is null
                    ? null
                    : await deadlines.GetLinkedFilePathAsync(deadline.Id).ConfigureAwait(true);

                rows.Add(new DeadlineRowViewModel(
                    deadline, _subject?.Name ?? "—", _subject?.ColorHex, now, linkedPath,
                    counts.GetValueOrDefault(deadline.Id)));
            }

            Items.Clear();
            foreach (var row in rows.OrderBy(r => r.IsDone).ThenBy(r => r.DueDate))
            {
                Items.Add(row);
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
        var subjectList = await subjects.GetAllAsync().ConfigureAwait(true);
        var editor = new DeadlineEditorViewModel(subjectList, existing: null, presetSubjectId: _subjectId);
        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText).ConfigureAwait(true))
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

        var subjectList = await subjects.GetAllAsync().ConfigureAwait(true);
        var editor = new DeadlineEditorViewModel(subjectList, row.Deadline);
        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText).ConfigureAwait(true))
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
            $"«{row.Title}» будет удалён вместе с текстом задания и ответа. Файлы и .md-копии в папке предмета останутся.").ConfigureAwait(true);
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

    /// <summary>«Готовиться» на карточке экзамена — сессия в режиме аврала по карточкам предмета.</summary>
    [RelayCommand]
    private async Task PrepareForExamAsync(DeadlineRowViewModel? row)
    {
        if (row is null || !row.IsExam)
        {
            return;
        }

        var today = await cram.GetTodayAsync(row.Deadline.SubjectId).ConfigureAwait(true);

        if (today.IsFailure)
        {
            toasts.Show("Аврал", today.Error.Message, ToastKind.Warning);
            return;
        }

        navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Cram,
            StudySessionFilter.Empty with { CardIds = today.Value, Order = StudyOrder.HardestFirst },
            Title: _subject is null ? "Аврал" : $"Аврал · {_subject.Name}"));
    }

    private async Task ApplyAsync(Func<Task<Result>> action, string errorTitle)
    {
        var result = await action().ConfigureAwait(true);
        if (result.IsFailure)
        {
            toasts.Show(errorTitle, result.Error.Message, ToastKind.Error);
            return;
        }

        scheduler.RequestReschedule();
        await LoadAsync(_subjectId).ConfigureAwait(true);
    }
}
