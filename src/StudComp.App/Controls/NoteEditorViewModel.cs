using System.Globalization;
using System.IO;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;
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
    private readonly INoteImageTrash _trash;
    private readonly IOptionsMonitor<NotesOptions> _notesOptions;
    private readonly UserSettingsProvider _settings;

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
        IToastService toasts,
        INoteImageTrash trash,
        IOptionsMonitor<NotesOptions> notesOptions,
        UserSettingsProvider settings)
    {
        _notes = notes;
        _markdown = markdown;
        _activity = activity;
        _dialogs = dialogs;
        _workspace = workspace;
        _subjects = subjects;
        _fileSystem = fileSystem;
        _toasts = toasts;
        _trash = trash;
        _notesOptions = notesOptions;
        _settings = settings;
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

    /// <summary>
    /// Подсказка в пустом поле. Зависит от того, что именно тут пишут: редактор один, а заметка,
    /// задание дедлайна и черновик отчёта – разные вещи, и «Текст заметки» в отчёте сбивает с толку.
    /// </summary>
    [ObservableProperty]
    private string _contentPlaceholder = "Текст заметки в Markdown…";

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

    partial void OnContentChanged(string value)
    {
        ScheduleSave();
        ReconcileImages(value);
    }

    partial void OnIsPreviewChanged(bool value)
    {
        // При загрузке документ соберёт сам LoadAsync — иначе он строился бы дважды.
        if (value && !_loading)
        {
            RefreshPreview();
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

        var leaving = _noteId;
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
            ShowTitle = note.Kind is not (NoteKind.DeadlineTask or NoteKind.DeadlineAnswer or NoteKind.ReportDraft);
            ContentPlaceholder = note.Kind switch
            {
                NoteKind.ReportDraft => "Текст отчёта в Markdown…",
                NoteKind.DeadlineTask => "Условие задания в Markdown…",
                NoteKind.DeadlineAnswer => "Ваш ответ в Markdown…",
                _ => "Текст заметки в Markdown…",
            };
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
                RefreshPreview();
            }
        }
        finally
        {
            _loading = false;
        }
        // Поле ввода одно на всю жизнь редактора, и присваивание Content выше легло в его стек
        // отмены. Без сброса Ctrl+Z в этой заметке откатил бы текст к тексту предыдущей, а
        // автосохранение записало бы чужой текст в базу.
        _editor?.ResetUndoHistory();

        // Стек отмены прошлой заметки только что умер — её картинки можно отпускать окончательно.
        if (leaving != note.Id)
        {
            CommitImageTrash(leaving);
        }

        // Текст ссылается на картинку, которой нет на диске, а в корзине она есть: приложение
        // закрылось между удалением и сохранением — возвращаем.
        await _trash.RestoreReferencedAsync(note.Id, Content).ConfigureAwait(true);

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

    /// <summary>
    /// Кто применяет правки к тексту. Подставляется код-behind'ом редактора; <see langword="null"/> —
    /// вьюмодель живёт без View (тесты, разбор заметки на карточки), тогда правки идут прямо в текст.
    /// </summary>
    private INoteTextEditor? _editor;

    /// <summary>Редактор отдаёт себя вьюмодели при смене <c>DataContext</c>.</summary>
    public void AttachEditor(INoteTextEditor? editor) => _editor = editor;

    /// <summary>
    /// Единственная точка, через которую вьюмодель меняет текст заметки. В режиме правки правка
    /// уходит в живое поле ввода и попадает в стек <c>Ctrl+Z</c> пошагово; в предпросмотре поля нет,
    /// и текст пишется целиком — это один шаг отмены на всю операцию, ровно нужная гранулярность.
    /// </summary>
    private void ApplyEdit(MarkdownEdit edit)
    {
        if (!IsPreview && _editor is { IsLive: true } editor && editor.TryApply(edit))
        {
            return;
        }

        Content = MarkdownEditing.Apply(Content, edit);
    }

    /// <summary>
    /// Сколько раз текст ссылался на каждую картинку в прошлый раз. Сверка этого среза с новым и
    /// двигает файлы: перехватить <c>Ctrl+Z</c> нельзя, но после отмены текст снова ссылается на
    /// картинку — и одного этого достаточно, чтобы вернуть файл.
    /// </summary>
    private IReadOnlyDictionary<string, int> _imageCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Одно правило закрывает разом удаление картинки, <c>Ctrl+Z</c>, <c>Ctrl+Y</c>, вырезание и
    /// вставку: ссылок на файл стало ноль — убрать его в корзину, ссылка вернулась — вернуть файл.
    /// </summary>
    private void ReconcileImages(string value)
    {
        // Ни раньше, ни теперь картинок нет — разбирать нечего.
        if (_imageCounts.Count == 0 && !MarkdownImageReconciliation.MightContainImages(value))
        {
            return;
        }

        var after = MarkdownImageReconciliation.CountReferences(value);
        if (_loading || _noteId == Guid.Empty)
        {
            _imageCounts = after;
            return;
        }

        var delta = MarkdownImageReconciliation.Compare(_imageCounts, after);
        _imageCounts = after;

        if (delta.Detached.Count > 0 || delta.Reattached.Count > 0)
        {
            _ = MoveImageFilesAsync(_noteId, delta);
        }
    }

    private async Task MoveImageFilesAsync(Guid noteId, ImageReferenceDelta delta)
    {
        try
        {
            foreach (var path in delta.Detached)
            {
                await _trash.StageAsync(noteId, path).ConfigureAwait(true);
            }

            foreach (var path in delta.Reattached)
            {
                await _trash.RestoreAsync(noteId, path).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            // Корзина картинок — удобство, а не данные заметки: ронять из-за неё редактор нельзя.
        }
    }

    /// <summary>
    /// Стек отмены этой заметки больше недостижим — всё, что лежит в её корзине, уходит в «Корзину»
    /// Windows. Тело <see cref="INoteImageTrash.CommitAsync"/> синхронное, поэтому задача
    /// завершается тут же.
    /// </summary>
    private void CommitImageTrash(Guid noteId)
    {
        if (noteId == Guid.Empty)
        {
            return;
        }

        try
        {
            _ = _trash.CommitAsync(noteId);
        }
        catch (Exception)
        {
            // См. выше: сбой уборки не должен мешать работе с заметкой.
        }
    }

    /// <summary>Пересобрать документ предпросмотра из текущего текста.</summary>
    private void RefreshPreview() => Preview = MarkdownFlowRenderer.Render(_markdown, Content, ImageBaseDirectory);

    /// <summary>
    /// <c>Ctrl+Z</c> из предпросмотра: поля ввода на экране нет, но стек отмены у него тот же самый,
    /// поэтому откат работает одинаково в обоих режимах.
    /// </summary>
    public void UndoFromPreview()
    {
        if (_editor?.TryUndo() == true)
        {
            RefreshPreview();
        }
    }

    /// <summary>
    /// Найти картинку в тексте по блоку, который пришёл из предпросмотра. Текст мог уйти вперёд —
    /// тогда правку применять нельзя, и пользователю честно об этом говорится.
    /// </summary>
    private MarkdownImageToken? ResolveImage(ImageBlock image)
    {
        var token = MarkdownImageEditing.Resolve(
            Content, image.SourceStart, image.SourceLength, image.PathOrBase64);

        if (token is null)
        {
            _toasts.Show("Изображение", "Текст заметки изменился — откройте картинку заново.", ToastKind.Warning);
        }

        return token;
    }

    /// <summary>Изменить ширину картинки. Значение зажимается, диалог сам не пропустит мусор.</summary>
    public async Task ResizeImageAsync(ImageBlock image)
    {
        if (ResolveImage(image) is null)
        {
            return;
        }

        var editor = new NoteImageSizeViewModel(image.Width);
        if (!await _dialogs.ShowEditorAsync(editor, "Размер изображения").ConfigureAwait(true))
        {
            return;
        }

        // Пока висел диалог, текст мог измениться — ищем картинку заново.
        if (ResolveImage(image) is not { } token
            || MarkdownImageEditing.SetWidth(Content, token, editor.Width) is not { } edit)
        {
            return;
        }

        ApplyEdit(edit);
        RefreshPreview();
        await FlushAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Удалить картинку из заметки. Сам файл уезжает в скрытую папку сверкой ссылок, поэтому
    /// отмена возвращает и ссылку, и файл.
    /// </summary>
    public async Task DeleteImageAsync(ImageBlock image)
    {
        if (ResolveImage(image) is not { } token)
        {
            return;
        }

        ApplyEdit(MarkdownImageEditing.Delete(Content, token));
        RefreshPreview();
        await FlushAsync().ConfigureAwait(true);
    }

    /// <summary>Передвинуть картинку на строку выше или ниже.</summary>
    public async Task MoveImageAsync(ImageBlock image, bool up)
    {
        if (ResolveImage(image) is not { } token)
        {
            return;
        }

        if (MarkdownImageEditing.MoveByLine(Content, token, up) is not { } edit)
        {
            return;
        }

        ApplyEdit(edit);
        RefreshPreview();
        await FlushAsync().ConfigureAwait(true);
    }

    public static bool IsSupportedImage(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff";

    /// <summary>
    /// Выбрать картинки с диска и вставить их копии в заметку. Диалог открывается там, где
    /// пользователь брал картинку в прошлый раз для этого предмета, а в первый раз — в папке
    /// предмета: именно там лежат снятые лекции, и искать путь заново незачем (new_addons.md §12).
    /// </summary>
    public async Task<int?> PickImageAsync(int caretIndex)
    {
        var paths = _dialogs.PickOpenFiles(
            "Вставить изображение",
            "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff",
            await ResolveImagePickerDirectoryAsync().ConfigureAwait(true));

        if (paths.Count == 0)
        {
            return null;
        }

        RememberImageFolder(paths[0]);
        return await InsertImagesAsync(paths, caretIndex).ConfigureAwait(true);
    }

    /// <summary>Где открыть диалог выбора картинки: последняя папка → папка предмета → учебная папка.</summary>
    private async Task<string?> ResolveImagePickerDirectoryAsync()
    {
        if (_subjectId is { } subjectId
            && _notesOptions.CurrentValue.LastImageFolders.TryGetValue(SubjectKey(subjectId), out var remembered)
            && _fileSystem.DirectoryExists(remembered))
        {
            return remembered;
        }

        if (_subjectId is { } id && await _subjects.GetByIdAsync(id).ConfigureAwait(true) is { } subject)
        {
            var directory = _workspace.GetSubjectDirectory(subject);
            if (!string.IsNullOrWhiteSpace(directory) && _fileSystem.DirectoryExists(directory))
            {
                return directory;
            }
        }

        return _workspace.HasStudyRoot ? _workspace.StudyRootPath : null;
    }

    private void RememberImageFolder(string pickedFile)
    {
        if (_subjectId is not { } subjectId || Path.GetDirectoryName(pickedFile) is not { Length: > 0 } folder)
        {
            return;
        }

        try
        {
            _settings.Update<NotesOptions>(
                NotesOptions.SectionName, options => options.LastImageFolders[SubjectKey(subjectId)] = folder);
        }
        catch (Exception)
        {
            // Запомнить папку — удобство; сорвать из-за него вставку картинки нельзя.
        }
    }

    private static string SubjectKey(Guid subjectId) => subjectId.ToString("N", CultureInfo.InvariantCulture);

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
                var edit = MarkdownImageEditing.Insert(Content, caret, relative, Path.GetFileNameWithoutExtension(source));
                ApplyEdit(edit);
                caret = edit.Start + edit.CaretOffset;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _toasts.Show("Изображение не вставлено", $"{Path.GetFileName(source)}: {ex.Message}", ToastKind.Error);
            }
        }
        RefreshPreview();
        await FlushAsync();
        return caret;
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
        var edit = MarkdownImageEditing.Insert(Content, Math.Clamp(caretIndex, 0, Content.Length), relativePath);
        ApplyEdit(edit);
        RefreshPreview();

        return edit.Start + edit.CaretOffset;
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

        RefreshPreview();
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

    private void Close()
    {
        CancelPendingSave();
        CommitImageTrash(_noteId);
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

    public void Dispose()
    {
        CancelPendingSave();
        CommitImageTrash(_noteId);
    }
}
