using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Behaviors;
using StudComp.Controls;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Resources;
using StudComp.Services;
using StudComp.ViewModels.Cards;

namespace StudComp.ViewModels.Organizer;

/// <summary>Параметр навигации на страницу работы над дедлайном.</summary>
public sealed record DeadlineWorkParameter(Guid DeadlineId);

/// <summary>Строка вложения на странице дедлайна.</summary>
public sealed class DeadlineAttachmentRowViewModel(DeadlineAttachmentInfo info)
{
    public Guid Id => info.Attachment.Id;

    public string FileName => info.Attachment.FileName;

    /// <summary>Расширение без точки, прописными — так ждёт <c>FileExtensionToSymbolConverter</c>.</summary>
    public string Extension => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();

    public string AbsolutePath => info.AbsolutePath;

    public bool Exists => info.Exists;

    public string AddedText => info.Attachment.AddedAt.LocalDateTime
        .ToString($"{AppDateFormat.ShortDate} HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// Страница работы над дедлайном (ревизия 2026-09-22 п.7): шапка со сроком и статусом, вкладка
/// «Задание» (текст с картинками + материалы) и вкладка «Ответ» (текст + файлы + «Сдано»).
/// Отдельная страница, а не диалог: текст с картинками и вложениями в модалке 560px неудобен.
/// </summary>
/// <remarks>
/// Тексты задания и ответа — служебные заметки дедлайна, поэтому редактор заметки переиспользуется
/// целиком (автосохранение, картинки, рисование, формулы). Два экземпляра <see cref="NoteEditorViewModel"/>
/// приходят из DI как Transient — по одному на вкладку. Файловая часть — <see cref="IDeadlineWorkService"/>;
/// страница ничего на диске не удаляет и не перезаписывает.
/// </remarks>
public sealed partial class DeadlineWorkViewModel : ObservableObject, INavigationAware
{
    private static readonly SolidColorBrush OverdueBrush = Frozen(Color.FromRgb(0xC0, 0x37, 0x2A));
    private static readonly SolidColorBrush DoneBrush = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush PendingBrush = Frozen(Color.FromRgb(0x6B, 0x6B, 0x6B));

    private const string AnyFileFilter = "Все файлы|*.*";

    private readonly IDeadlineService _deadlines;
    private readonly ISubjectService _subjects;
    private readonly IDeadlineWorkService _work;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IShellLauncher _shell;
    private readonly INavigationService _navigation;
    private readonly INotificationScheduler _scheduler;
    private readonly ICramPlanService _cram;

    private Guid _deadlineId;
    private Deadline? _deadline;
    private Subject? _subject;

    public DeadlineWorkViewModel(
        IDeadlineService deadlines,
        ISubjectService subjects,
        IDeadlineWorkService work,
        IDialogService dialogs,
        IToastService toasts,
        IShellLauncher shell,
        INavigationService navigation,
        INotificationScheduler scheduler,
        ICramPlanService cram,
        NoteEditorViewModel taskEditor,
        NoteEditorViewModel answerEditor)
    {
        _deadlines = deadlines;
        _subjects = subjects;
        _work = work;
        _dialogs = dialogs;
        _toasts = toasts;
        _shell = shell;
        _navigation = navigation;
        _scheduler = scheduler;
        _cram = cram;
        TaskEditor = taskEditor;
        AnswerEditor = answerEditor;
    }

    public NoteEditorViewModel TaskEditor { get; }

    public NoteEditorViewModel AnswerEditor { get; }

    public ObservableCollection<DeadlineAttachmentRowViewModel> TaskAttachments { get; } = [];

    public ObservableCollection<DeadlineAttachmentRowViewModel> AnswerAttachments { get; } = [];

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subjectName = string.Empty;

    [ObservableProperty]
    private Brush _accentBrush = PendingBrush;

    [ObservableProperty]
    private string _metaText = string.Empty;

    [ObservableProperty]
    private string _dueText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private Brush _statusBrush = PendingBrush;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private bool _isOverdue;

    [ObservableProperty]
    private bool _isExam;

    [ObservableProperty]
    private bool _hasAnswer;

    [ObservableProperty]
    private string _answeredText = string.Empty;

    [ObservableProperty]
    private bool _hasFolder;

    [ObservableProperty]
    private bool _hasLinkedFile;

    [ObservableProperty]
    private string _linkedFileName = string.Empty;

    [ObservableProperty]
    private string? _linkedFilePath;

    [ObservableProperty]
    private bool _isTaskDragOver;

    [ObservableProperty]
    private bool _isAnswerDragOver;

    [ObservableProperty]
    private string _importStatusText = string.Empty;

    public bool HasTaskAttachments => TaskAttachments.Count > 0 || HasLinkedFile;

    public bool HasAnswerAttachments => AnswerAttachments.Count > 0;

    public string ToggleDoneText => IsDone ? "Вернуть в работу" : "Готово";

    public bool CanGoBack => _navigation.CanGoBack;

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not DeadlineWorkParameter { DeadlineId: var id })
        {
            return;
        }

        _deadlineId = id;
        _ = LoadAsync();
    }

    public void OnNavigatedFrom()
    {
        // Автосохранение работает с задержкой — уходя со страницы, дописываем оба текста.
        _ = TaskEditor.FlushAsync();
        _ = AnswerEditor.FlushAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var deadline = await _deadlines.GetByIdAsync(_deadlineId).ConfigureAwait(true);
            if (deadline is null)
            {
                _toasts.Show("Дедлайн не найден", "Возможно, он был удалён.", ToastKind.Warning);
                _navigation.GoBack();
                return;
            }

            _deadline = deadline;
            _subject = await _subjects.GetByIdAsync(deadline.SubjectId).ConfigureAwait(true);
            ApplyHeader(deadline);

            var notes = await _work.EnsureNotesAsync(_deadlineId).ConfigureAwait(true);
            if (notes.IsSuccess)
            {
                await TaskEditor.LoadAsync(notes.Value.TaskNoteId).ConfigureAwait(true);
                await AnswerEditor.LoadAsync(notes.Value.AnswerNoteId).ConfigureAwait(true);
            }
            else
            {
                _toasts.Show("Текст дедлайна", notes.Error.Message, ToastKind.Warning);
            }

            await ReloadAttachmentsAsync().ConfigureAwait(true);
            HasFolder = await _work.GetFolderPathAsync(_deadlineId).ConfigureAwait(true) is not null;
            IsLoaded = true;
            OnPropertyChanged(nameof(CanGoBack));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyHeader(Deadline deadline)
    {
        var now = DateTimeOffset.Now;
        Title = deadline.Title;
        SubjectName = _subject?.Name ?? "—";
        AccentBrush = SubjectColor.BrushFor(_subject?.ColorHex);

        var typeText = OrganizerChoices.DeadlineTypes.First(t => t.Value == deadline.Type).Display;
        var priorityText = OrganizerChoices.Priorities.First(p => p.Value == deadline.Priority).Display;
        MetaText = $"{typeText} · приоритет: {priorityText.ToLowerInvariant()}";

        DueText = "Срок: " + RelativeDayFormatter.Format(deadline.DueDate);

        IsDone = deadline.Status == DeadlineStatus.Done;
        IsOverdue = deadline.Status == DeadlineStatus.Pending && deadline.DueDate < now;
        IsExam = deadline.Type == DeadlineType.Exam && !IsDone;

        (StatusText, StatusBrush) = IsDone
            ? ("Готово", DoneBrush)
            : IsOverdue
                ? ("Просрочен", OverdueBrush)
                : ("В работе", PendingBrush);

        HasAnswer = deadline.AnsweredAt is not null;
        AnsweredText = deadline.AnsweredAt is { } answeredAt
            ? "Сдано " + answeredAt.LocalDateTime.ToString($"{AppDateFormat.ShortDate} HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;

        OnPropertyChanged(nameof(ToggleDoneText));
    }

    private async Task ReloadAttachmentsAsync()
    {
        var all = await _work.GetAttachmentsAsync(_deadlineId).ConfigureAwait(true);

        TaskAttachments.Clear();
        AnswerAttachments.Clear();
        foreach (var info in all)
        {
            var row = new DeadlineAttachmentRowViewModel(info);
            (info.Attachment.Role == DeadlineAttachmentRole.Answer ? AnswerAttachments : TaskAttachments).Add(row);
        }

        if (_deadline?.LinkedFileRecordId is not null)
        {
            LinkedFilePath = await _deadlines.GetLinkedFilePathAsync(_deadlineId).ConfigureAwait(true);
            LinkedFileName = LinkedFilePath is null ? string.Empty : Path.GetFileName(LinkedFilePath);
            HasLinkedFile = LinkedFilePath is not null;
        }
        else
        {
            LinkedFilePath = null;
            LinkedFileName = string.Empty;
            HasLinkedFile = false;
        }

        OnPropertyChanged(nameof(HasTaskAttachments));
        OnPropertyChanged(nameof(HasAnswerAttachments));
    }

    // ── Шапка ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void GoBack()
    {
        if (_navigation.CanGoBack)
        {
            _navigation.GoBack();
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (_deadline is null)
        {
            return;
        }

        var subjectList = await _subjects.GetAllAsync().ConfigureAwait(true);
        var editor = new DeadlineEditorViewModel(subjectList, _deadline);
        if (await _dialogs.ShowEditorAsync(editor, editor.HeaderText).ConfigureAwait(true))
        {
            await ApplyAsync(() => _deadlines.UpdateAsync(editor.ToModel()), "Не удалось сохранить дедлайн").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleDoneAsync()
    {
        if (_deadline is null)
        {
            return;
        }

        var target = IsDone ? DeadlineStatus.Pending : DeadlineStatus.Done;
        await ApplyAsync(() => _deadlines.SetStatusAsync(_deadlineId, target), "Не удалось изменить статус").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = await _work.EnsureFolderAsync(_deadlineId).ConfigureAwait(true);
        if (folder.IsFailure)
        {
            _toasts.Show("Папка дедлайна", folder.Error.Message, ToastKind.Warning);
            return;
        }

        HasFolder = true;
        if (!_shell.RevealInExplorer(folder.Value))
        {
            _toasts.Show("Папка дедлайна", "Не удалось открыть Проводник.", ToastKind.Warning);
        }
    }

    /// <summary>«Готовиться» для экзамена — сессия в режиме аврала по карточкам предмета.</summary>
    [RelayCommand]
    private async Task PrepareForExamAsync()
    {
        if (_deadline is null || !IsExam)
        {
            return;
        }

        var today = await _cram.GetTodayAsync(_deadline.SubjectId).ConfigureAwait(true);
        if (today.IsFailure)
        {
            _toasts.Show("Аврал", today.Error.Message, ToastKind.Warning);
            return;
        }

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Cram,
            StudySessionFilter.Empty with { CardIds = today.Value, Order = StudyOrder.HardestFirst },
            Title: _subject is null ? "Аврал" : $"Аврал · {_subject.Name}"));
    }

    // ── Ответ ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SubmitAsync()
    {
        await AnswerEditor.FlushAsync().ConfigureAwait(true);
        await ApplyAsync(() => _work.SubmitAsync(_deadlineId), "Не удалось отметить сдачу").ConfigureAwait(true);
        if (HasAnswer)
        {
            _toasts.Show("Сдано", $"«{Title}» отмечен сданным.", ToastKind.Success);
        }
    }

    [RelayCommand]
    private Task ReopenAsync() =>
        ApplyAsync(() => _work.ReopenAsync(_deadlineId), "Не удалось вернуть в работу");

    // ── Вложения ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddTaskFilesAsync() => await PickAndAddAsync(DeadlineAttachmentRole.Task).ConfigureAwait(true);

    [RelayCommand]
    private async Task AddAnswerFilesAsync() => await PickAndAddAsync(DeadlineAttachmentRole.Answer).ConfigureAwait(true);

    [RelayCommand]
    private async Task DropTaskAsync(FileDropRequest? request)
    {
        IsTaskDragOver = false;
        if (request is not null)
        {
            await AddAsync(DeadlineAttachmentRole.Task, request.Paths, request.ForcedMode).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DropAnswerAsync(FileDropRequest? request)
    {
        IsAnswerDragOver = false;
        if (request is not null)
        {
            await AddAsync(DeadlineAttachmentRole.Answer, request.Paths, request.ForcedMode).ConfigureAwait(true);
        }
    }

    private async Task PickAndAddAsync(DeadlineAttachmentRole role)
    {
        var paths = _dialogs.PickOpenFiles(
            role == DeadlineAttachmentRole.Answer ? "Файлы ответа" : "Материалы задания", AnyFileFilter);
        if (paths.Count > 0)
        {
            // Файлы, выбранные диалогом, копируем: пользователь редко хочет, чтобы выбор из
            // «Загрузок» что-то оттуда унёс. Перетаскивание спрашивает, как в «Файлах» Хаба.
            await AddAsync(role, paths, ImportMode.Copy).ConfigureAwait(true);
        }
    }

    private async Task AddAsync(DeadlineAttachmentRole role, IReadOnlyList<string> paths, ImportMode? forcedMode)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var mode = forcedMode ?? await AskModeAsync().ConfigureAwait(true);
        IsBusy = true;
        ImportStatusText = "Добавление файлов…";
        try
        {
            var result = await _work.AddAttachmentsAsync(_deadlineId, role, paths, mode).ConfigureAwait(true);
            if (result.IsFailure)
            {
                _toasts.Show("Файлы не добавлены", result.Error.Message, ToastKind.Error);
                return;
            }

            HasFolder = true;
            ReportSummary(result.Value, mode);
            await ReloadAttachmentsAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            ImportStatusText = string.Empty;
        }
    }

    private async Task<ImportMode> AskModeAsync()
    {
        var move = await _dialogs.ConfirmAsync(
            "Перенести или скопировать?",
            "«Перенести» уберёт файлы из исходной папки, «Скопировать» оставит их на месте.",
            primaryButton: "Перенести").ConfigureAwait(true);

        return move ? ImportMode.Move : ImportMode.Copy;
    }

    private void ReportSummary(ImportSummary summary, ImportMode mode)
    {
        var verb = mode == ImportMode.Move ? "перенесено" : "скопировано";
        var parts = new List<string> { $"{verb}: {summary.Imported}" };
        if (summary.SkippedDuplicates > 0)
        {
            parts.Add($"дубликатов пропущено: {summary.SkippedDuplicates}");
        }

        if (summary.Failed > 0)
        {
            parts.Add($"не удалось: {summary.Failed}");
        }

        _toasts.Show(
            "Файлы добавлены",
            string.Join(" · ", parts),
            summary.Failed > 0 ? ToastKind.Warning : ToastKind.Success);
    }

    [RelayCommand]
    private void OpenAttachment(DeadlineAttachmentRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (!row.Exists || !_shell.OpenFile(row.AbsolutePath))
        {
            _toasts.Show("Не удалось открыть файл", "Возможно, его переместили или удалили.", ToastKind.Warning);
        }
    }

    [RelayCommand]
    private void RevealAttachment(DeadlineAttachmentRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (!row.Exists || !_shell.RevealInExplorer(row.AbsolutePath))
        {
            _toasts.Show("Не удалось показать файл", "Возможно, его переместили или удалили.", ToastKind.Warning);
        }
    }

    /// <summary>Убрать из списка — только запись учёта, файл остаётся в папке дедлайна (§14).</summary>
    [RelayCommand]
    private async Task RemoveAttachmentAsync(DeadlineAttachmentRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var result = await _work.RemoveAttachmentAsync(row.Id).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось убрать файл", result.Error.Message, ToastKind.Error);
            return;
        }

        _toasts.Show("Убрано из списка", $"{row.FileName} остаётся в папке дедлайна.", ToastKind.Info);
        await ReloadAttachmentsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenLinkedFile()
    {
        if (LinkedFilePath is not { Length: > 0 } path)
        {
            return;
        }

        if (!_shell.OpenFile(path))
        {
            _toasts.Show("Не удалось открыть файл", "Возможно, его переместили или удалили.", ToastKind.Warning);
        }
    }

    private async Task ApplyAsync(Func<Task<Result>> action, string errorTitle)
    {
        var result = await action().ConfigureAwait(true);
        if (result.IsFailure)
        {
            _toasts.Show(errorTitle, result.Error.Message, ToastKind.Error);
            return;
        }

        _scheduler.RequestReschedule();

        var refreshed = await _deadlines.GetByIdAsync(_deadlineId).ConfigureAwait(true);
        if (refreshed is not null)
        {
            _deadline = refreshed;
            ApplyHeader(refreshed);
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
