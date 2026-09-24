using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Core.Abstractions.Workspace;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Где на диске живёт заметка: папка для <c>.md</c>-копии, имя этого файла и папка для картинок.
/// Единственный источник правды — обычные заметки и служебные заметки дедлайна лежат по-разному.
/// </summary>
public sealed record NoteStorage(string Directory, string ExportFileName, string ImagesDirectory)
{
    public string ExportPath => Path.Combine(Directory, ExportFileName);
}

/// <summary>
/// Операции над заметками (new_addons.md §1.9): валидация + <see cref="Result"/> поверх
/// <see cref="INoteRepository"/>.
/// </summary>
public interface INoteService
{
    /// <summary>
    /// Папки заметки на диске; <see langword="null"/> — учебная папка не задана либо у дедлайна
    /// ещё нет своей папки (текст при этом всё равно хранится в базе).
    /// </summary>
    Task<NoteStorage?> GetStorageAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Note>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<IReadOnlyList<Note>> GetRecentAsync(int take, CancellationToken ct = default);

    Task<Note?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Note>> GetByLinkedPathAsync(string linkedPath, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(Note note, CancellationToken ct = default);

    /// <summary>
    /// Точка автосохранения редактора: трогает только заголовок, текст и <c>UpdatedAt</c>.
    /// Отдельный метод, а не общий <c>UpdateAsync</c>, именно поэтому — дебаунс-сохранение не должно
    /// иметь физической возможности затереть привязки заметки к файлу, папке или паре.
    /// </summary>
    Task<Result> UpdateContentAsync(Guid id, string title, string markdown, CancellationToken ct = default);

    Task<Result> SetPinnedAsync(Guid id, bool pinned, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<Result<string>> ExportMarkdownAsync(Guid id, CancellationToken ct = default);
}

internal sealed class NoteService(
    INoteRepository notes,
    ISubjectRepository subjects,
    IStudyWorkspace? workspace = null,
    IDeadlineRepository? deadlines = null) : INoteService
{
    private const int MaxTitleLength = 300;

    /// <summary>Подпапка предмета с заметками и подпапка с их картинками — как в Phase 13.7.</summary>
    private const string NotesFolder = "Заметки";
    private const string ImagesFolder = "Рисунки";
    private const string DeadlinesFolder = "Дедлайны";

    public async Task<NoteStorage?> GetStorageAsync(Guid id, CancellationToken ct = default)
    {
        var note = await notes.GetByIdAsync(id, ct).ConfigureAwait(false);
        return note is null ? null : await ResolveStorageAsync(note, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Обычная заметка: <c>Заметки/Заметка_&lt;id&gt;.md</c> и <c>Рисунки/</c> внутри папки предмета
    /// (без предмета — внутри <c>Учебная папка/Заметки</c>). Служебная заметка дедлайна: папка
    /// самого дедлайна, файл <c>Задание.md</c> / <c>Ответ.md</c>, картинки — в её <c>Рисунки/</c>.
    /// </summary>
    private async Task<NoteStorage?> ResolveStorageAsync(Note note, CancellationToken ct)
    {
        if (workspace is null)
        {
            return null;
        }

        var subject = note.SubjectId is { } subjectId
            ? await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false)
            : null;
        var subjectDirectory = subject is not null ? workspace.GetSubjectDirectory(subject) : null;

        if (note.DeadlineId is { } deadlineId)
        {
            var deadline = deadlines is null ? null : await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
            if (deadline?.FolderName is not { Length: > 0 } folderName || string.IsNullOrWhiteSpace(subjectDirectory))
            {
                return null;
            }

            var deadlineDirectory = Path.Combine(subjectDirectory, DeadlinesFolder, folderName);
            var fileName = note.Kind == NoteKind.DeadlineAnswer ? "Ответ.md" : "Задание.md";
            return new NoteStorage(deadlineDirectory, fileName, Path.Combine(deadlineDirectory, ImagesFolder));
        }

        if (!string.IsNullOrWhiteSpace(subjectDirectory))
        {
            return new NoteStorage(
                Path.Combine(subjectDirectory, NotesFolder),
                $"Заметка_{note.Id:N}.md",
                Path.Combine(subjectDirectory, ImagesFolder));
        }

        if (!workspace.HasStudyRoot)
        {
            return null;
        }

        var notesDirectory = Path.Combine(workspace.StudyRootPath, NotesFolder);
        return new NoteStorage(notesDirectory, $"Заметка_{note.Id:N}.md", Path.Combine(notesDirectory, ImagesFolder));
    }

    public async Task<Result<string>> ExportMarkdownAsync(Guid id, CancellationToken ct = default)
    {
        var note = await notes.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (note is null) return Result<string>.Failure("organizer.note_not_found", "Заметка не найдена.");
        var storage = await ResolveStorageAsync(note, ct).ConfigureAwait(false);
        if (storage is null)
            return Result<string>.Failure("note.no_folder", "Текст сохранён в базе. Укажите учебную папку для копии .md.");
        try
        {
            var directory = storage.Directory;
            Directory.CreateDirectory(directory);
            var path = storage.ExportPath;
            var markdown = MarkdownLocalImages.Rewrite(note.ContentMarkdown, image =>
            {
                var absolute = Path.IsPathRooted(image) ? image
                    : Path.Combine(workspace!.StudyRootPath, image);
                return Path.GetRelativePath(directory, absolute);
            });
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, $"# {note.Title}\n\n{markdown}", ct).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return Result<string>.Success(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<string>.Failure("note.export_failed", $"Текст сохранён в базе, но копия .md не записана: {ex.Message}");
        }
    }

    public Task<IReadOnlyList<Note>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default) =>
        notes.GetBySubjectAsync(subjectId, ct);

    public Task<IReadOnlyList<Note>> GetRecentAsync(int take, CancellationToken ct = default) =>
        notes.GetRecentAsync(take <= 0 ? 1 : take, ct);

    public Task<Note?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        notes.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Note>> GetByLinkedPathAsync(string linkedPath, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(linkedPath)
            ? Task.FromResult<IReadOnlyList<Note>>([])
            : notes.GetByLinkedPathAsync(linkedPath, ct);

    public async Task<Result<Guid>> CreateAsync(Note note, CancellationToken ct = default)
    {
        Guard.NotNull(note);

        if (note.SubjectId is { } subjectId
            && await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false) is null)
        {
            return Result<Guid>.Failure("organizer.subject_not_found", "Предмет не найден.");
        }

        note.Id = note.Id == Guid.Empty ? Guid.NewGuid() : note.Id;
        note.Title = NormalizeTitle(note.Title);
        note.ContentMarkdown ??= string.Empty;

        var now = DateTimeOffset.Now;
        note.CreatedAt = note.CreatedAt == default ? now : note.CreatedAt;
        note.UpdatedAt = now;

        await notes.AddAsync(note, ct).ConfigureAwait(false);
        if (workspace is not null) await ExportMarkdownAsync(note.Id, ct).ConfigureAwait(false);
        return Result<Guid>.Success(note.Id);
    }

    public async Task<Result> UpdateContentAsync(
        Guid id, string title, string markdown, CancellationToken ct = default)
    {
        var note = await notes.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (note is null)
        {
            return Result.Failure("organizer.note_not_found", "Заметка не найдена.");
        }

        note.Title = NormalizeTitle(title);
        note.ContentMarkdown = markdown ?? string.Empty;
        note.UpdatedAt = DateTimeOffset.Now;

        await notes.UpdateAsync(note, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetPinnedAsync(Guid id, bool pinned, CancellationToken ct = default)
    {
        var note = await notes.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (note is null)
        {
            return Result.Failure("organizer.note_not_found", "Заметка не найдена.");
        }

        note.IsPinned = pinned;
        await notes.UpdateAsync(note, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await notes.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.note_not_found", "Заметка не найдена.");
        }

        await notes.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>
    /// Пустой заголовок — обычное дело при автосохранении только что созданной заметки, поэтому это
    /// не ошибка валидации: подставляем нейтральное название и обрезаем по длине колонки.
    /// </summary>
    private static string NormalizeTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Без названия";
        }

        return trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength] : trimmed;
    }
}
