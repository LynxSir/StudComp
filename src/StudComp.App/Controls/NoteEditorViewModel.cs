using System.Globalization;
using System.IO;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels;

namespace StudComp.Controls;

/// <summary>Выделение в заметке, из которого просят сделать карточку (new_addons.md §7.2).</summary>
public sealed record CardFromSelectionRequestedEventArgs(string Front, string Back, Guid? SubjectId, Guid NoteId);

/// <summary>
/// Редактор заметки (new_addons.md §1.9): Markdown, режим предпросмотра и автосохранение.
/// Кнопки «Сохранить» нет и не будет — правка уходит в БД сама.
/// </summary>
/// <remarks>
/// Дебаунс сделан отменяемой задержкой, как <c>RuleEditorViewModel.SchedulePreview</c> в Архивариусе,
/// а не таймером: так последняя правка всегда выигрывает, а незавершённое сохранение снимается.
/// Принудительная запись — на потерю фокуса и на уход со страницы (<c>FlushAsync</c>).
/// </remarks>
public sealed partial class NoteEditorViewModel : ObservableObject, IDisposable
{
    /// <summary>Пауза после последнего нажатия клавиши, после которой заметка уходит в БД.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private readonly INoteService _notes;
    private readonly IMarkdownDocumentModelBuilder _markdown;
    private readonly IActivityRepository _activity;
    private readonly IDialogService _dialogs;
    private readonly IStudyWorkspace _workspace;
    private readonly ISubjectService _subjects;
    private readonly IFileSystem _fileSystem;
    private readonly IToastService _toasts;

    private CancellationTokenSource? _saveCts;
    private Guid _noteId;
    private Guid? _subjectId;
    private bool _loading;
    private bool _dirty;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private int _revision;
    private int _loadRevision;

    public NoteEditorViewModel(
        INoteService notes,
        IMarkdownDocumentModelBuilder markdown,
        IActivityRepository activity,
        IDialogService dialogs,
        IStudyWorkspace workspace,
        ISubjectService subjects,
        IFileSystem fileSystem,
        IToastService toasts)
    {
        _notes = notes;
        _markdown = markdown;
        _activity = activity;
        _dialogs = dialogs;
        _workspace = workspace;
        _subjects = subjects;
        _fileSystem = fileSystem;
        _toasts = toasts;
    }

    /// <summary>Срабатывает после каждого удачного сохранения — списки заметок перечитывают себя.</summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Пользователь выбрал «Сделать карточку» на выделении (new_addons.md §7.2). Событие, а не прямой
    /// вызов <c>ICardService</c>/<c>IDialogService</c> — так редактор не обрастает зависимостями
    /// Картотеки, а форму создания открывает хостящий VM, у которого они уже есть (тот же приём, что
    /// у <c>HubFilesViewModel.NoteCreated</c>).
    /// </summary>
    public event EventHandler<CardFromSelectionRequestedEventArgs>? CardFromSelectionRequested;

    /// <summary>
    /// Разобрать выделенный текст на будущую карточку: перевод строки в выделении разделяет лицо и
    /// оборот, без переноса — весь текст идёт в лицо, а оборот остаётся пустым и сфокусированным.
    /// </summary>
    public void RequestCardFromSelection(string selectedText)
    {
        var trimmed = (selectedText ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var newlineAt = trimmed.IndexOf('\n');
        var front = newlineAt < 0 ? trimmed : trimmed[..newlineAt].Trim();
        var back = newlineAt < 0 ? string.Empty : trimmed[(newlineAt + 1)..].Trim();

        CardFromSelectionRequested?.Invoke(
            this, new CardFromSelectionRequestedEventArgs(front, back, _subjectId, _noteId));
    }

    /// <summary>Открыта ли заметка. Пока нет — панель редактора показывает пустое состояние.</summary>
    [ObservableProperty]
    private bool _hasNote;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>
    /// Режим предпросмотра вместо ввода. Заметка с текстом открывается именно в нём
    /// (new_addons.md §11.5), правка включается кликом по содержимому.
    /// </summary>
    [ObservableProperty]
    private bool _isPreview = true;

    [ObservableProperty]
    private FlowDocument? _preview;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Подпись привязки: файл, папка или пара, к которым сделана заметка.</summary>
    [ObservableProperty]
    private string _linkText = string.Empty;

    [ObservableProperty]
    private string _storageText = string.Empty;

    /// <summary>
    /// Показывать ли поле заголовка. У служебных заметок дедлайна («Задание»/«Ответ») заголовок
    /// фиксирован и редактировать его незачем.
    /// </summary>
    [ObservableProperty]
    private bool _showTitle = true;

    private string? _markdownFilePath;

    [RelayCommand]
    private void OpenNoteFolder()
    {
        if (_markdownFilePath is { } path && File.Exists(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else
            _toasts.Show("Хранение заметки", StorageText, ToastKind.Info);
    }

    private async Task UpdateMarkdownCopyAsync(Guid id)
    {
        var exported = await _notes.ExportMarkdownAsync(id);
        if (id != _noteId) return;
        _markdownFilePath = exported.IsSuccess ? exported.Value : null;
        StorageText = exported.IsSuccess ? $"Копия .md: {exported.Value}" : exported.Error.Message;
    }

    partial void OnTitleChanged(string value) => ScheduleSave();

    partial void OnContentChanged(string value) => ScheduleSave();

    partial void OnIsPreviewChanged(bool value)
    {
        // При загрузке документ соберёт сам LoadAsync — иначе он строился бы дважды.
        if (value && !_loading)
        {
            Preview = MarkdownFlowRenderer.Render(_markdown, Content, ImageBaseDirectory);
        }
    }

    /// <summary>Открыть заметку в редакторе. Несохранённое от предыдущей дописывается до конца.</summary>
    public async Task LoadAsync(Guid noteId)
    {
        // Та же заметка уже открыта — перечитывать нечего, и это не оптимизация. Список заметок
        // перестраивается после каждого автосохранения и заново выставляет выбранную строку, из-за
        // чего сюда прилетает повторный вызов на ту же заметку. Перечитывание вернуло бы режим
        // просмотра посреди набора текста и затёрло бы символы, набранные за время записи в БД.
        if (HasNote && noteId == _noteId)
        {
            return;
        }

        var loadRevision = ++_loadRevision;
        await FlushAsync().ConfigureAwait(true);
        if (loadRevision != _loadRevision) return;
        if (_dirty)
        {
            _toasts.Show("Заметка не сохранена", "Не удалось сохранить текущую заметку. Повторите попытку перед переходом.", ToastKind.Error);
            return;
        }

        var note = await _notes.GetByIdAsync(noteId).ConfigureAwait(true);
        if (loadRevision != _loadRevision) return;
        if (note is null)
        {
            Close();
            return;
        }

        _loading = true;
        try
        {
            _noteId = note.Id;
            _subjectId = note.SubjectId;
            ShowTitle = note.Kind is not (NoteKind.DeadlineTask or NoteKind.DeadlineAnswer);
            Title = note.Title;
            Content = note.ContentMarkdown;
            LinkText = DescribeLink(note);
            StatusText = $"Изменено {note.UpdatedAt.LocalDateTime.ToString("dd.MM HH:mm", Ru)}";
            HasNote = true;
            _dirty = false;

            // Пустая заметка открывается сразу в правке: показывать предпросмотр пустоты незачем,
            // а «Быстрая заметка» с Дашборда именно пустую и создаёт.
            IsPreview = !string.IsNullOrWhiteSpace(Content);

            if (IsPreview)
            {
                Preview = MarkdownFlowRenderer.Render(_markdown, Content, ImageBaseDirectory);
            }
        }
        finally
        {
            _loading = false;
        }
        await UpdateMarkdownCopyAsync(note.Id);
    }

    /// <summary>Закрыть редактор, дописав несохранённое.</summary>
    public async Task CloseAsync()
    {
        await FlushAsync().ConfigureAwait(true);
        if (!_dirty) Close();
    }

    /// <summary>
    /// Немедленно записать несохранённое. Зовётся на потере фокуса, при смене заметки и при уходе
    /// со страницы — чтобы правка не потерялась вместе с окном.
    /// </summary>
    public async Task FlushAsync()
    {
        CancelPendingSave();

        if (_noteId == Guid.Empty)
        {
            return;
        }

        await SaveAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void TogglePreview()
    {
        if (IsPreview)
        {
            BeginEditAt(0);
            return;
        }

        IsPreview = true;
    }

    /// <summary>Шпаргалка по разметке (new_addons.md §11.4) — одна на всё приложение.</summary>
    [RelayCommand]
    private Task ShowMarkdownHelpAsync() =>
        _dialogs.ShowInfoAsync(new MarkdownHelpViewModel(), "Разметка Markdown");

    /// <summary>
    /// Просят перейти к правке — обычно кликом по содержимому предпросмотра (new_addons.md §11.5).
    /// Каретку выставляет View: поле ввода в этот момент ещё скрыто и фокус не примет.
    /// </summary>
    public event EventHandler<int>? EditRequested;

    /// <summary>Включить режим правки и попросить View поставить каретку в это место.</summary>
    public void BeginEditAt(int caretIndex)
    {
        IsPreview = false;
        EditRequested?.Invoke(this, Math.Clamp(caretIndex, 0, Content.Length));
    }

    /// <summary>
    /// Где в исходнике искать кусок текста, по которому кликнули в предпросмотре. Оценка
    /// заведомо приблизительная: размеченный текст и исходник совпадают не всюду (в заголовке нет
    /// решёток, формула отрисована глифами). Не нашли — ставим каретку в начало, это честнее,
    /// чем угадать не то место.
    /// </summary>
    public int LocateInSource(string? probe)
    {
        var needle = (probe ?? string.Empty).Trim();
        if (needle.Length == 0)
        {
            return 0;
        }

        // Длинный кусок ищем по началу: в конце он мог быть обрезан переносом строки.
        if (needle.Length > 60)
        {
            needle = needle[..60];
        }

        var index = Content.IndexOf(needle, StringComparison.Ordinal);
        return index < 0 ? 0 : index;
    }

    /// <summary>
    /// Папка, относительно которой резолвятся относительные пути картинок в предпросмотре
    /// (new_addons.md §12, Phase 13.7) — та же точка отсчёта, что и у файлов рисунков.
    /// </summary>
    private string? ImageBaseDirectory => _workspace.HasStudyRoot ? _workspace.StudyRootPath : null;

    public static bool IsSupportedImage(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff";

    public async Task<int?> PickImageAsync(int caretIndex)
    {
        var path = _dialogs.PickOpenFile("Вставить изображение", "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff");
        return path is null ? null : await InsertImagesAsync([path], caretIndex);
    }

    public async Task<int?> InsertImagesAsync(IEnumerable<string> paths, int caretIndex)
    {
        if (!HasNote) return null;
        var noteId = _noteId;
        var directory = await ResolveAttachmentsDirectoryAsync();
        if (directory is null || noteId != _noteId) return null;
        var caret = Math.Clamp(caretIndex, 0, Content.Length);
        foreach (var source in paths.Where(IsSupportedImage))
        {
            try
            {
                _fileSystem.CreateDirectory(directory);
                var name = Path.GetFileNameWithoutExtension(source);
                if (name.Length > 80) name = name[..80];
                var destination = Path.Combine(directory,
                    $"{name}_{Guid.NewGuid():N}{Path.GetExtension(source)}");
                // Keep the original safe; the note owns its independent copy.
                _fileSystem.Copy(source, destination);
                var relative = _workspace.ResolveRelative(destination) ?? destination;
                var insertion = SpliceImageMarkdown(Content, caret, relative);
                Content = insertion.Content;
                caret = insertion.CaretAfter;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _toasts.Show("Изображение не вставлено", $"{Path.GetFileName(source)}: {ex.Message}", ToastKind.Error);
            }
        }
        Preview = MarkdownFlowRenderer.Render(_markdown, Content, ImageBaseDirectory);
        await FlushAsync();
        return caret;
    }

    public async Task ResizeImageAsync(ImageBlock image)
    {
        var original = Content;
        var editor = new NoteImageSizeViewModel(image.Width);
        if (await _dialogs.ShowEditorAsync(editor, "Размер изображения") && Content == original)
        {
            Content = MarkdownImageSize.SetWidth(Content, image.SourceStart, image.SourceLength, editor.Width);
            Preview = MarkdownFlowRenderer.Render(_markdown, Content, ImageBaseDirectory);
            await FlushAsync();
        }
    }

    /// <summary>
    /// Нарисовать новую картинку и вставить её markdown-ссылкой в позицию курсора (new_addons.md §12,
    /// Phase 13.7). Возвращает позицию курсора сразу после вставленного текста — код-behind ставит
    /// туда каретку, если сейчас режим правки; ничего не вставлено — <see langword="null"/>.
    /// </summary>
    public async Task<int?> InsertDrawingAsync(int caretIndex)
    {
        var directory = await ResolveAttachmentsDirectoryAsync().ConfigureAwait(true);
        if (directory is null)
        {
            return null;
        }

        var dialogViewModel = new NoteDrawingDialogViewModel();
        var confirmed = await _dialogs
            .ShowEditorAsync(dialogViewModel, "Рисование", "Вставить", dialogMaxWidth: 960)
            .ConfigureAwait(true);
        if (!confirmed || dialogViewModel.Document is not { Elements.Count: > 0 } document
            || dialogViewModel.PngBytes is not { Length: > 0 } pngBytes)
        {
            return null;
        }

        _fileSystem.CreateDirectory(directory);
        var baseName = UniqueBaseName(directory);
        var pngPath = Path.Combine(directory, baseName + ".png");
        var jsonPath = Path.Combine(directory, baseName + ".json");

        WriteFile(pngPath, pngBytes);
        WriteFile(jsonPath, System.Text.Encoding.UTF8.GetBytes(document.ToJson()));

        var relativePath = _workspace.ResolveRelative(pngPath) ?? pngPath;
        var (newContent, caretAfter) = SpliceImageMarkdown(Content, Math.Clamp(caretIndex, 0, Content.Length), relativePath);
        Content = newContent;
        Preview = MarkdownFlowRenderer.Render(_markdown, Content, ImageBaseDirectory);

        return caretAfter;
    }

    /// <summary>
    /// Открыть уже вставленный рисунок повторно (клик по картинке в предпросмотре, new_addons.md §12).
    /// Путь текста заметки не меняется — меняется только сам файл на диске, поэтому это
    /// единственное намеренное исключение из «никогда не перезаписывать» (ADR §16.30): это собственный
    /// сгенерированный артефакт программы, а не файл, принесённый пользователем.
    /// </summary>
    public async Task EditDrawingAtAsync(string imagePath)
    {
        var pngPath = ResolveAbsoluteImagePath(imagePath);
        var jsonPath = pngPath is null ? null : Path.ChangeExtension(pngPath, ".json");

        if (pngPath is null || jsonPath is null || !_fileSystem.FileExists(jsonPath))
        {
            _toasts.Show(
                "Рисование", "Этот рисунок нельзя отредактировать — он не был создан в этой программе.", ToastKind.Warning);
            return;
        }

        string json;
        using (var stream = _fileSystem.OpenRead(jsonPath))
        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
        {
            json = await reader.ReadToEndAsync().ConfigureAwait(true);
        }

        if (!NoteDrawingDocument.TryParse(json, out var initial))
        {
            _toasts.Show("Рисование", "Файл рисунка повреждён — открыть для правки не удалось.", ToastKind.Warning);
            return;
        }

        var dialogViewModel = new NoteDrawingDialogViewModel(initial);
        var confirmed = await _dialogs
            .ShowEditorAsync(dialogViewModel, "Рисование", "Сохранить", dialogMaxWidth: 960)
            .ConfigureAwait(true);
        if (!confirmed || dialogViewModel.Document is null || dialogViewModel.PngBytes is not { Length: > 0 } pngBytes)
        {
            return;
        }

        WriteFile(pngPath, pngBytes);
        WriteFile(jsonPath, System.Text.Encoding.UTF8.GetBytes(dialogViewModel.Document.ToJson()));

        Preview = MarkdownFlowRenderer.Render(_markdown, Content, ImageBaseDirectory);
    }

    /// <summary>
    /// Папка для картинок и рисунков заметки. Где именно она лежит, решает <see cref="INoteService"/>:
    /// у обычной заметки — <c>Рисунки/</c> предмета, у служебной заметки дедлайна — папка дедлайна.
    /// </summary>
    private async Task<string?> ResolveAttachmentsDirectoryAsync()
    {
        var storage = HasNote ? await _notes.GetStorageAsync(_noteId).ConfigureAwait(true) : null;
        if (storage is null)
        {
            _toasts.Show("Изображение", "Укажите учебную папку или папку предмета в настройках.", ToastKind.Warning);
            return null;
        }

        return storage.ImagesDirectory;
    }

    private string? ResolveAbsoluteImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        if (Path.IsPathRooted(imagePath))
        {
            return imagePath;
        }

        return _workspace.HasStudyRoot ? Path.Combine(_workspace.StudyRootPath, imagePath) : null;
    }

    private string UniqueBaseName(string directory)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var baseName = $"Рисунок_{stamp}";
        var candidate = baseName;
        var suffix = 2;
        while (_fileSystem.FileExists(Path.Combine(directory, candidate + ".png")))
        {
            candidate = $"{baseName}_{suffix++}";
        }

        return candidate;
    }

    private void WriteFile(string path, byte[] bytes)
    {
        using var stream = _fileSystem.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Вставить markdown-картинку в позицию курсора. Переносы строк вокруг — только для читаемости
    /// исходника: картинка обрывает абзац независимо от окружения (см. <c>MarkdownDocumentModelBuilder</c>).
    /// </summary>
    private static (string Content, int CaretAfter) SpliceImageMarkdown(string content, int caretIndex, string relativePath)
    {
        // Путь почти всегда содержит пробелы (папка предмета обычно называется в несколько слов) —
        // по CommonMark путь ссылки без угловых скобок не может содержать пробел, иначе Markdig не
        // распознаёт вставку как картинку вовсе и показывает её буквальным текстом (new_addons.md
        // §12, найдено на реальном запуске). Угловые скобки <...> — штатный экранирующий синтаксис,
        // разбирается уже подключённым Markdig без изменений в самом парсере/рендерере.
        var snippet = $"![Рисунок](<{MarkdownLocalImages.Encode(relativePath)}>)";
        var before = caretIndex > 0 && content[caretIndex - 1] != '\n' ? "\n" : string.Empty;
        var after = caretIndex < content.Length && content[caretIndex] != '\n' ? "\n" : string.Empty;
        var insertion = before + snippet + after;

        return (content.Insert(caretIndex, insertion), caretIndex + before.Length + snippet.Length);
    }

    private void Close()
    {
        CancelPendingSave();
        _noteId = Guid.Empty;
        _subjectId = null;
        _dirty = false;
        HasNote = false;
        Title = string.Empty;
        Content = string.Empty;
        LinkText = string.Empty;
        StatusText = string.Empty;
        StorageText = string.Empty;
        _markdownFilePath = null;
        Preview = null;
    }

    private void ScheduleSave()
    {
        if (_loading || _noteId == Guid.Empty)
        {
            return;
        }

        _dirty = true;
        _revision++;
        StatusText = "Сохранение…";

        CancelPendingSave();
        var cts = new CancellationTokenSource();
        _saveCts = cts;

        _ = DelayThenSaveAsync(cts.Token);
    }

    private async Task DelayThenSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SaveDelay, ct).ConfigureAwait(true);
            await SaveAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Пользователь продолжил печатать — сохранит следующая попытка.
        }
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            var id = _noteId;
            if (id == Guid.Empty || !_dirty) return;

            var revision = _revision;
            var result = await _notes.UpdateContentAsync(id, Title, Content).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusText = "Не удалось сохранить";
                return;
            }

            _dirty = _revision != revision;
            StatusText = _dirty ? "Сохранение…" : $"Сохранено {DateTime.Now.ToString("HH:mm", Ru)}";
            await UpdateMarkdownCopyAsync(id);
            await WriteActivityAsync(id).ConfigureAwait(true);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = $"Не удалось сохранить: {ex.Message}";
        }
        finally { _saveGate.Release(); }
    }

    /// <summary>
    /// Пишет в ленту активности не чаще раза в пять минут на заметку: посекундный автосейв за одну
    /// лекцию иначе вытеснил бы из ленты вообще всё остальное (ретеншн — 500 записей).
    /// </summary>
    private async Task WriteActivityAsync(Guid noteId)
    {
        if (DateTimeOffset.Now - _lastActivityWrite < ActivityThrottle)
        {
            return;
        }

        _lastActivityWrite = DateTimeOffset.Now;

        try
        {
            await _activity.AddAsync(new ActivityEntry
            {
                Id = Guid.NewGuid(),
                Kind = ActivityKind.NoteEdited,
                Timestamp = DateTimeOffset.Now,
                SubjectId = _subjectId,
                RefId = noteId,
                Title = Title,
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Лента активности — украшение Дашборда, ронять из-за неё сохранение заметки нельзя.
        }
    }

    private static readonly TimeSpan ActivityThrottle = TimeSpan.FromMinutes(5);

    private DateTimeOffset _lastActivityWrite = DateTimeOffset.MinValue;

    private static string DescribeLink(Note note) => note.Kind switch
    {
        NoteKind.FileNote when note.LinkedPath is { Length: > 0 } path => $"к файлу: {path}",
        NoteKind.FolderNote when note.LinkedPath is { Length: > 0 } path => $"к папке: {path}",
        NoteKind.Lecture when note.ClassDate is { } date => $"с пары {date.ToString("dd.MM.yyyy", Ru)}",
        _ => string.Empty,
    };

    private void CancelPendingSave()
    {
        var cts = _saveCts;
        _saveCts = null;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    public void Dispose() => CancelPendingSave();
}
