using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Modules.Organizer.Services;

/// <summary>Идентификаторы служебных заметок дедлайна: текст задания и текст ответа.</summary>
public sealed record DeadlineNotes(Guid TaskNoteId, Guid AnswerNoteId);

/// <summary>Вложение вместе с абсолютным путём и признаком «файл на месте».</summary>
public sealed record DeadlineAttachmentInfo(DeadlineAttachment Attachment, string AbsolutePath, bool Exists);

/// <summary>
/// Работа над дедлайном (new_addons.md, ревизия 2026-09-22 п.7): папка дедлайна в папке предмета,
/// служебные заметки «Задание» и «Ответ», вложения-материалы и файлы ответа, отметка «Сдано».
/// </summary>
/// <remarks>
/// Раскладка на диске: <c>&lt;Папка предмета&gt;/Дедлайны/&lt;FolderName&gt;/</c> с подпапками
/// <c>Материалы</c>, <c>Ответ</c> и <c>Рисунки</c>; <c>.md</c>-копии текстов кладёт туда же
/// <see cref="INoteService.ExportMarkdownAsync"/>. Файловая работа делегируется
/// <see cref="IWorkspaceImportService"/> — тем же путём, что и импорт в «Файлы» Хаба, поэтому
/// конфликты имён и дубликаты решаются одинаково. Сервис никогда не удаляет и не перезаписывает
/// файлы пользователя (ARCHITECTURE §14): «убрать из списка» снимает только запись учёта.
/// </remarks>
public interface IDeadlineWorkService
{
    /// <summary>Создать (если ещё нет) папку дедлайна и вернуть её абсолютный путь.</summary>
    Task<Result<string>> EnsureFolderAsync(Guid deadlineId, CancellationToken ct = default);

    /// <summary>Путь папки дедлайна без создания; <see langword="null"/> — папки ещё нет или нет учебной папки.</summary>
    Task<string?> GetFolderPathAsync(Guid deadlineId, CancellationToken ct = default);

    /// <summary>Найти или создать служебные заметки задания и ответа.</summary>
    Task<Result<DeadlineNotes>> EnsureNotesAsync(Guid deadlineId, CancellationToken ct = default);

    /// <summary>Приложить файлы: перенести или скопировать в папку роли и занести в учёт.</summary>
    Task<Result<ImportSummary>> AddAttachmentsAsync(
        Guid deadlineId,
        DeadlineAttachmentRole role,
        IReadOnlyList<string> sourcePaths,
        ImportMode mode,
        CancellationToken ct = default);

    /// <summary>Убрать вложение из списка. Файл остаётся на диске.</summary>
    Task<Result> RemoveAttachmentAsync(Guid attachmentId, CancellationToken ct = default);

    Task<IReadOnlyList<DeadlineAttachmentInfo>> GetAttachmentsAsync(Guid deadlineId, CancellationToken ct = default);

    /// <summary>Число вложений по дедлайнам одним запросом — для бейджей на карточках списка.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetAttachmentCountsAsync(CancellationToken ct = default);

    /// <summary>Отметить работу сданной: дата сдачи + статус «Готово» (решение владельца).</summary>
    Task<Result> SubmitAsync(Guid deadlineId, CancellationToken ct = default);

    /// <summary>Вернуть в работу: снять дату сдачи и статус «Готово».</summary>
    Task<Result> ReopenAsync(Guid deadlineId, CancellationToken ct = default);
}

internal sealed class DeadlineWorkService(
    IDeadlineRepository deadlines,
    IDeadlineAttachmentRepository attachments,
    INoteRepository notes,
    INoteService noteService,
    ISubjectRepository subjects,
    IStudyWorkspace workspace,
    IWorkspaceImportService importService,
    IWorkspaceFileLedger ledger,
    IFileSystem fileSystem) : IDeadlineWorkService
{
    private const string DeadlinesFolder = "Дедлайны";
    private const string TaskFolder = "Материалы";
    private const string AnswerFolder = "Ответ";
    private const string TaskNoteTitle = "Задание";
    private const string AnswerNoteTitle = "Ответ";

    /// <summary>Создание папки и служебных заметок должно быть атомарным на уровне процесса.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<string>> EnsureFolderAsync(Guid deadlineId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var deadline = await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
            if (deadline is null)
            {
                return Result<string>.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
            }

            var subjectDirectory = await ResolveSubjectDirectoryAsync(deadline, ct).ConfigureAwait(false);
            if (subjectDirectory is null)
            {
                return Result<string>.Failure(
                    "organizer.deadline_no_folder",
                    "Укажите учебную папку в настройках — материалы дедлайна хранятся в папке предмета.");
            }

            var deadlinesRoot = Path.Combine(subjectDirectory, DeadlinesFolder);

            if (string.IsNullOrWhiteSpace(deadline.FolderName))
            {
                var siblings = await deadlines.GetBySubjectAsync(deadline.SubjectId, ct).ConfigureAwait(false);
                var taken = siblings
                    .Where(x => x.Id != deadline.Id && !string.IsNullOrWhiteSpace(x.FolderName))
                    .Select(x => x.FolderName!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                deadline.FolderName = PickFolderName(deadlinesRoot, SubjectFolder.Sanitize(deadline.Title), taken);
                await deadlines.UpdateAsync(deadline, ct).ConfigureAwait(false);
            }

            var folder = Path.Combine(deadlinesRoot, deadline.FolderName);
            try
            {
                fileSystem.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Result<string>.Failure("organizer.deadline_folder_failed", $"Не удалось создать папку дедлайна: {ex.Message}");
            }

            return Result<string>.Success(folder);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetFolderPathAsync(Guid deadlineId, CancellationToken ct = default)
    {
        var deadline = await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
        if (deadline?.FolderName is not { Length: > 0 } folderName)
        {
            return null;
        }

        var subjectDirectory = await ResolveSubjectDirectoryAsync(deadline, ct).ConfigureAwait(false);
        return subjectDirectory is null ? null : Path.Combine(subjectDirectory, DeadlinesFolder, folderName);
    }

    public async Task<Result<DeadlineNotes>> EnsureNotesAsync(Guid deadlineId, CancellationToken ct = default)
    {
        var deadline = await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
        if (deadline is null)
        {
            return Result<DeadlineNotes>.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
        }

        // Папка нужна до создания заметок: их .md-копии пишутся туда сразу при создании.
        // Без учебной папки заметки всё равно создаются — текст живёт в базе.
        await EnsureFolderAsync(deadlineId, ct).ConfigureAwait(false);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await notes.GetByDeadlineAsync(deadlineId, ct).ConfigureAwait(false);
            var task = existing.FirstOrDefault(x => x.Kind == NoteKind.DeadlineTask);
            var answer = existing.FirstOrDefault(x => x.Kind == NoteKind.DeadlineAnswer);

            var taskId = task?.Id ?? await CreateNoteAsync(deadline, NoteKind.DeadlineTask, TaskNoteTitle, ct).ConfigureAwait(false);
            var answerId = answer?.Id ?? await CreateNoteAsync(deadline, NoteKind.DeadlineAnswer, AnswerNoteTitle, ct).ConfigureAwait(false);

            return Result<DeadlineNotes>.Success(new DeadlineNotes(taskId, answerId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<ImportSummary>> AddAttachmentsAsync(
        Guid deadlineId,
        DeadlineAttachmentRole role,
        IReadOnlyList<string> sourcePaths,
        ImportMode mode,
        CancellationToken ct = default)
    {
        Guard.NotNull(sourcePaths);

        var folder = await EnsureFolderAsync(deadlineId, ct).ConfigureAwait(false);
        if (folder.IsFailure)
        {
            return Result<ImportSummary>.Failure(folder.Error);
        }

        var deadline = await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
        if (deadline is null)
        {
            return Result<ImportSummary>.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
        }

        var target = Path.Combine(folder.Value, role == DeadlineAttachmentRole.Answer ? AnswerFolder : TaskFolder);
        var summary = await importService
            .ImportAsync(sourcePaths, target, mode, DuplicatePolicy.KeepBoth, progress: null, ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.Now;
        var rows = summary.Items
            .Where(x => x.Outcome is ImportItemOutcome.Imported or ImportItemOutcome.RenamedDueToConflict
                        && x.FinalPath is not null)
            .Select(x => new DeadlineAttachment
            {
                Id = Guid.NewGuid(),
                DeadlineId = deadlineId,
                Role = role,
                FileName = Path.GetFileName(x.FinalPath!),
                RelativePath = workspace.ResolveRelative(x.FinalPath!) ?? x.FinalPath!,
                AddedAt = now,
            })
            .ToArray();

        await attachments.AddManyAsync(rows, ct).ConfigureAwait(false);

        // Хаб «Файлы» и лента активности видят приложенные файлы так же, как обычный импорт.
        await ledger.RecordImportAsync(deadline.SubjectId, summary, ct).ConfigureAwait(false);

        return Result<ImportSummary>.Success(summary);
    }

    public async Task<Result> RemoveAttachmentAsync(Guid attachmentId, CancellationToken ct = default)
    {
        if (await attachments.GetByIdAsync(attachmentId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.attachment_not_found", "Вложение не найдено.");
        }

        // Только запись учёта: файл пользователя остаётся на диске (ARCHITECTURE §14).
        await attachments.DeleteAsync(attachmentId, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<IReadOnlyList<DeadlineAttachmentInfo>> GetAttachmentsAsync(Guid deadlineId, CancellationToken ct = default)
    {
        var rows = await attachments.GetByDeadlineAsync(deadlineId, ct).ConfigureAwait(false);
        var result = new List<DeadlineAttachmentInfo>(rows.Count);
        foreach (var row in rows)
        {
            var absolute = ResolveAbsolute(row.RelativePath);
            result.Add(new DeadlineAttachmentInfo(row, absolute, fileSystem.FileExists(absolute)));
        }

        return result;
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetAttachmentCountsAsync(CancellationToken ct = default) =>
        attachments.CountsByDeadlineAsync(ct);

    public Task<Result> SubmitAsync(Guid deadlineId, CancellationToken ct = default) =>
        UpdateStatusAsync(deadlineId, DateTimeOffset.Now, DeadlineStatus.Done, ct);

    public Task<Result> ReopenAsync(Guid deadlineId, CancellationToken ct = default) =>
        UpdateStatusAsync(deadlineId, null, DeadlineStatus.Pending, ct);

    private async Task<Result> UpdateStatusAsync(Guid deadlineId, DateTimeOffset? answeredAt, DeadlineStatus status, CancellationToken ct)
    {
        var deadline = await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
        if (deadline is null)
        {
            return Result.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
        }

        deadline.AnsweredAt = answeredAt;
        deadline.Status = status;
        await deadlines.UpdateAsync(deadline, ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Guid> CreateNoteAsync(Deadline deadline, NoteKind kind, string title, CancellationToken ct)
    {
        var note = new Note
        {
            Id = Guid.NewGuid(),
            SubjectId = deadline.SubjectId,
            DeadlineId = deadline.Id,
            Kind = kind,
            Title = title,
            ContentMarkdown = string.Empty,
        };

        var created = await noteService.CreateAsync(note, ct).ConfigureAwait(false);
        return created.IsSuccess ? created.Value : note.Id;
    }

    private async Task<string?> ResolveSubjectDirectoryAsync(Deadline deadline, CancellationToken ct)
    {
        var subject = await subjects.GetByIdAsync(deadline.SubjectId, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return null;
        }

        var directory = workspace.GetSubjectDirectory(subject);
        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    private string ResolveAbsolute(string relativePath) =>
        Path.IsPathRooted(relativePath) || !workspace.HasStudyRoot
            ? relativePath
            : Path.Combine(workspace.StudyRootPath, relativePath);

    /// <summary>
    /// Имя папки по заголовку; занятое другим дедлайном предмета или уже существующее на диске
    /// получает суффикс « (2)», « (3)»… — чужая папка никогда не переиспользуется.
    /// </summary>
    private string PickFolderName(string deadlinesRoot, string preferred, HashSet<string> taken)
    {
        var candidate = preferred;
        for (var index = 2; index < 1000; index++)
        {
            var busy = taken.Contains(candidate) || fileSystem.DirectoryExists(Path.Combine(deadlinesRoot, candidate));
            if (!busy)
            {
                return candidate;
            }

            candidate = $"{preferred} ({index})";
        }

        return $"{preferred} {Guid.NewGuid():N}";
    }
}
