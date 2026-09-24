using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Behaviors;
using StudComp.Controls;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels.Cards;

namespace StudComp.ViewModels.Organizer.Hub;

/// <summary>Строка файла в списке текущей подпапки предмета.</summary>
public sealed class SubjectFileRowViewModel(string path, long size, DateTimeOffset modified)
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public string Path { get; } = path;

    public string Name { get; } = System.IO.Path.GetFileName(path);

    /// <summary>Расширение без точки — по нему подбирается иконка типа файла.</summary>
    public string Extension { get; } = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public string InfoText { get; } =
        $"{FormatSize(size)} · {modified.LocalDateTime.ToString("dd.MM.yyyy HH:mm", Ru)}";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} МБ",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} ГБ",
    };
}

/// <summary>
/// Вкладка «Файлы» Хаба предмета (new_addons.md §5): дерево подпапок, список файлов текущей папки,
/// панель предпросмотра и импорт перетаскиванием из проводника.
/// </summary>
/// <remarks>
/// Импорт разнесён по слоям: файловую работу делает <see cref="IWorkspaceImportService"/> в
/// инфраструктуре (он же держит инвариант §14), а учёт — <c>FileRecord</c> и лента активности —
/// ведётся здесь: <c>Infrastructure</c> не ссылается на <c>Data</c> (ARCHITECTURE §5.1).
/// </remarks>
public sealed partial class HubFilesViewModel(
    IStudyWorkspace workspace,
    IWorkspaceImportService importService,
    IFileSystem fileSystem,
    ISubjectService subjects,
    INoteService notes,
    IWorkspaceFileLedger ledger,
    IShellLauncher shell,
    IDialogService dialogs,
    IToastService toasts,
    FilePreviewViewModel preview,
    ICardService cards,
    ICardDeckService cardDecks,
    ICardTagService cardTags) : ObservableObject
{
    private Guid _subjectId;
    private Subject? _subject;
    private string _rootPath = string.Empty;

    public FilePreviewViewModel Preview { get; } = preview;

    /// <summary>Дерево подпапок: корень — сама папка предмета.</summary>
    public ObservableCollection<FileNodeViewModel> Tree { get; } = [];

    public ObservableCollection<SubjectFileRowViewModel> Files { get; } = [];

    [ObservableProperty]
    private FileNodeViewModel? _selectedNode;

    [ObservableProperty]
    private SubjectFileRowViewModel? _selectedFile;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Учебная папка выбрана и папка предмета существует.</summary>
    [ObservableProperty]
    private bool _hasFolder;

    [ObservableProperty]
    private string _folderPathText = string.Empty;

    /// <summary>Оверлей «перетащите файлы сюда» во время перетаскивания.</summary>
    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private string _dropHintText = string.Empty;

    [ObservableProperty]
    private string _importStatusText = string.Empty;

    public bool HasFiles => Files.Count > 0;

    partial void OnSelectedNodeChanged(FileNodeViewModel? value)
    {
        if (value is not null)
        {
            _ = LoadFilesAsync(value.Path);
        }
    }

    partial void OnSelectedFileChanged(SubjectFileRowViewModel? value) => Preview.Load(value?.Path);

    public async Task LoadAsync(Guid subjectId)
    {
        _subjectId = subjectId;
        _subject = await subjects.GetByIdAsync(subjectId).ConfigureAwait(true);

        Tree.Clear();
        Files.Clear();
        Preview.Load(null);

        if (_subject is null)
        {
            HasFolder = false;
            return;
        }

        _rootPath = workspace.GetSubjectDirectory(_subject);
        DropHintText = $"Перенести файлы в «{_subject.Name}»";

        if (string.IsNullOrWhiteSpace(_rootPath))
        {
            HasFolder = false;
            FolderPathText = "Учебная папка не выбрана — задайте её в настройках.";
            return;
        }

        FolderPathText = _rootPath;
        HasFolder = fileSystem.DirectoryExists(_rootPath);
        if (!HasFolder)
        {
            return;
        }

        var root = new FileNodeViewModel(
            fileSystem, _rootPath, _subject.Name, isRoot: true, onSelected: HandleNodeSelected);
        Tree.Add(root);
        await root.LoadChildrenAsync().ConfigureAwait(true);
        root.IsExpanded = true;
        root.IsSelected = true;
        SelectedNode = root;
    }

    /// <summary>
    /// Узел дерева сообщил, что его выбрали (new_addons.md §7.1) — переключаем «текущую папку».
    /// </summary>
    private void HandleNodeSelected(FileNodeViewModel node) => SelectedNode = node;

    /// <summary>Создать папку предмета и скелет подпапок, если их ещё нет.</summary>
    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        if (_subject is null)
        {
            return;
        }

        try
        {
            workspace.EnsureSubjectScaffold(_subject);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            toasts.Show("Не удалось создать папку", ex.Message, ToastKind.Error);
            return;
        }

        await LoadAsync(_subjectId);
    }

    [RelayCommand]
    private void OpenFile(SubjectFileRowViewModel? row)
    {
        if (row is not null)
        {
            shell.OpenFile(row.Path);
        }
    }

    [RelayCommand]
    private void RevealFile(SubjectFileRowViewModel? row)
    {
        if (row is not null)
        {
            shell.RevealInExplorer(row.Path);
        }
    }

    /// <summary>Создать заметку, привязанную к файлу (new_addons.md §1.9).</summary>
    [RelayCommand]
    private async Task AddNoteForFileAsync(SubjectFileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // Путь храним относительно учебной папки: она переезжает, а абсолютный путь протух бы молча.
        var relative = workspace.ResolveRelative(row.Path) ?? row.Path;

        var result = await notes.CreateAsync(new Note
        {
            SubjectId = _subjectId,
            Kind = NoteKind.FileNote,
            Title = row.Name,
            ContentMarkdown = string.Empty,
            LinkedPath = relative,
        });

        if (result.IsFailure)
        {
            toasts.Show("Не удалось создать заметку", result.Error.Message, ToastKind.Error);
            return;
        }

        NoteCreated?.Invoke(this, result.Value);
    }

    /// <summary>Заметка создана из контекст-меню файла — Хаб переключится на вкладку «Заметки».</summary>
    public event EventHandler<Guid>? NoteCreated;

    /// <summary>Карточка создана из контекст-меню файла (new_addons.md §7.4) — Хаб переключится на «Карточки».</summary>
    public event EventHandler<Guid>? CardCreated;

    /// <summary>
    /// Создать карточку к файлу: форма открывается предзаполненной именем файла в источнике, ничего
    /// не создаётся молча. <c>SourceFileRecordId</c> не заполняется — файлы этой вкладки читаются
    /// напрямую с диска, а не через учёт <c>FileRecord</c>.
    /// </summary>
    [RelayCommand]
    private async Task CreateCardForFileAsync(SubjectFileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var allSubjects = await subjects.GetAllAsync().ConfigureAwait(true);
        var allDecks = await cardDecks.GetAllAsync().ConfigureAwait(true);
        var allTags = await cardTags.GetAllAsync().ConfigureAwait(true);

        var editor = new CardEditorViewModel(
            null, allSubjects, allDecks, allTags, cards, preselectedSubjectId: _subjectId, dialogs: dialogs)
        {
            Source = row.Name,
        };

        if (!await dialogs.ShowEditorAsync(editor, editor.HeaderText).ConfigureAwait(true))
        {
            return;
        }

        var result = await cards.CreateAsync(editor.ToModel(), editor.ParseTags()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось создать карточку", result.Error.Message, ToastKind.Error);
            return;
        }

        CardCreated?.Invoke(this, result.Value);
    }

    /// <summary>Файлы сброшены на панель — команда для <c>FileDropBehavior</c>.</summary>
    [RelayCommand]
    private Task Drop(FileDropRequest? request) =>
        request is null ? Task.CompletedTask : ImportAsync(request.Paths, request.ForcedMode);

    /// <summary>
    /// Импорт перетащенных файлов. Режим выбирает пользователь: <c>Ctrl</c> — копировать,
    /// <c>Shift</c> — перенести, иначе спрашиваем диалогом (new_addons.md §1.11).
    /// </summary>
    public async Task ImportAsync(IReadOnlyList<string> paths, ImportMode? forcedMode)
    {
        IsDragOver = false;

        if (paths.Count == 0 || SelectedNode is null)
        {
            return;
        }

        var mode = forcedMode ?? await AskModeAsync().ConfigureAwait(true);
        if (mode is not { } chosen)
        {
            return;
        }

        var target = SelectedNode.Path;
        IsBusy = true;
        ImportStatusText = "Импорт…";
        try
        {
            var progress = new Progress<ImportProgress>(p =>
                ImportStatusText = p.Total == 0
                    ? string.Empty
                    : $"Импорт: {Math.Min(p.Done + 1, p.Total)} из {p.Total}");

            var summary = await importService
                .ImportAsync(paths, target, chosen, DuplicatePolicy.Skip, progress)
                .ConfigureAwait(true);

            await ledger.RecordImportAsync(_subjectId, summary).ConfigureAwait(true);
            ReportSummary(summary, chosen);

            SelectedNode.Invalidate();
            await LoadFilesAsync(target).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            ImportStatusText = string.Empty;
        }
    }

    private async Task<ImportMode?> AskModeAsync()
    {
        // Маленький выбор из двух действий — ровно то, для чего есть ConfirmAsync с двумя кнопками.
        var move = await dialogs.ConfirmAsync(
            "Перенести или скопировать?",
            "«Перенести» уберёт файлы из исходной папки, «Скопировать» оставит их на месте.",
            primaryButton: "Перенести");

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

        toasts.Show(
            summary.WasCancelled ? "Импорт отменён" : "Импорт завершён",
            string.Join(" · ", parts),
            summary.Failed > 0 ? ToastKind.Warning : ToastKind.Success);
    }

    private async Task LoadFilesAsync(string directory)
    {
        IsBusy = true;
        try
        {
            var rows = await Task.Run(() =>
            {
                try
                {
                    return fileSystem.EnumerateFiles(directory)
                        .Select(path => new SubjectFileRowViewModel(
                            path, fileSystem.GetFileSize(path), fileSystem.GetLastWriteTimeUtc(path)))
                        .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToArray();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return [];
                }
            }).ConfigureAwait(true);

            Files.Clear();
            foreach (var row in rows)
            {
                Files.Add(row);
            }

            SelectedFile = null;
            Preview.Load(null);
            OnPropertyChanged(nameof(HasFiles));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
