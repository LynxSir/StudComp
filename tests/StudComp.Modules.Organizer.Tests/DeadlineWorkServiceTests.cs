using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Workspace;
using StudComp.Modules.Organizer.Services;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Работа над дедлайном (ревизия 2026-09-22 п.7) на настоящем temp-SQLite и настоящей временной
/// учебной папке: папка дедлайна, служебные заметки, вложения, «Сдано». Главный инвариант — ни одна
/// операция не удаляет и не перезаписывает файлы пользователя (ARCHITECTURE §14), поэтому там, где
/// это важно, сравниваются снимки папок до и после.
/// </summary>
public sealed class DeadlineWorkServiceTests : OrganizerDatabaseTestBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rubrica-deadline-work-" + Guid.NewGuid().ToString("N"));
    private readonly string _inbox;

    public DeadlineWorkServiceTests()
    {
        Directory.CreateDirectory(_root);
        _inbox = Path.Combine(_root, "_inbox");
        Directory.CreateDirectory(_inbox);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private (IDeadlineWorkService Work, INoteService Notes, IDeadlineAttachmentRepository Attachments, Workspace Workspace) Build(bool withStudyRoot = true)
    {
        var workspace = new Workspace(withStudyRoot ? _root : null);
        var attachments = new DeadlineAttachmentRepository(Factory);
        var notes = new NoteService(NoteRepo, SubjectRepo, workspace, DeadlineRepo);
        var fileSystem = new SystemFileSystem();
        var import = new WorkspaceImportService(fileSystem, new FileHasher(fileSystem), NullLogger<WorkspaceImportService>.Instance);
        var ledger = new WorkspaceFileLedger(FileRecordRepo, new ActivityRepository(Factory), NullLogger<WorkspaceFileLedger>.Instance);
        var work = new DeadlineWorkService(DeadlineRepo, attachments, NoteRepo, notes, SubjectRepo, workspace, import, ledger, fileSystem);
        return (work, notes, attachments, workspace);
    }

    private async Task<Guid> SeedDeadlineAsync(Guid subjectId, string title = "ЛР №3: Фильтры")
    {
        var created = await Deadlines.CreateAsync(new Deadline
        {
            SubjectId = subjectId,
            Title = title,
            DueDate = DateTimeOffset.Now.AddDays(3),
            Type = DeadlineType.Homework,
            Priority = DeadlinePriority.Normal,
        });
        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private string WriteInboxFile(string name, string content = "данные")
    {
        var path = Path.Combine(_inbox, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string[] Snapshot(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order().ToArray()
            : [];

    [Fact]
    public async Task Folder_is_created_lazily_inside_the_subject_and_the_name_is_fixed_once()
    {
        var (work, _, _, _) = Build();
        var subjectId = await SeedSubjectAsync("Физика");
        var deadlineId = await SeedDeadlineAsync(subjectId, "ЛР №3: Фильтры");

        Assert.Null(await work.GetFolderPathAsync(deadlineId));
        Assert.Null((await DeadlineRepo.GetByIdAsync(deadlineId))!.FolderName);

        var folder = await work.EnsureFolderAsync(deadlineId);

        Assert.True(folder.IsSuccess);
        Assert.Equal(Path.Combine(_root, "Физика", "Дедлайны", "ЛР №3_ Фильтры"), folder.Value);
        Assert.True(Directory.Exists(folder.Value));
        Assert.Equal("ЛР №3_ Фильтры", (await DeadlineRepo.GetByIdAsync(deadlineId))!.FolderName);

        // Переименование дедлайна папку не трогает — иначе файлы потерялись бы.
        var deadline = (await DeadlineRepo.GetByIdAsync(deadlineId))!;
        deadline.Title = "Совсем другое имя";
        await DeadlineRepo.UpdateAsync(deadline);
        Assert.Equal(folder.Value, (await work.EnsureFolderAsync(deadlineId)).Value);
    }

    [Fact]
    public async Task Two_deadlines_with_the_same_title_get_different_folders()
    {
        var (work, _, _, _) = Build();
        var subjectId = await SeedSubjectAsync();
        var first = await SeedDeadlineAsync(subjectId, "Контрольная");
        var second = await SeedDeadlineAsync(subjectId, "Контрольная");

        var firstFolder = (await work.EnsureFolderAsync(first)).Value;
        var secondFolder = (await work.EnsureFolderAsync(second)).Value;

        Assert.NotEqual(firstFolder, secondFolder);
        Assert.EndsWith("Контрольная (2)", secondFolder);
    }

    [Fact]
    public async Task Without_a_study_root_the_folder_is_refused_but_notes_still_exist()
    {
        var (work, notes, _, _) = Build(withStudyRoot: false);
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId);

        var folder = await work.EnsureFolderAsync(deadlineId);
        Assert.True(folder.IsFailure);
        Assert.Equal("organizer.deadline_no_folder", folder.Error.Code);

        var pair = await work.EnsureNotesAsync(deadlineId);
        Assert.True(pair.IsSuccess);
        Assert.NotNull(await notes.GetByIdAsync(pair.Value.TaskNoteId));
        Assert.Null(await notes.GetStorageAsync(pair.Value.TaskNoteId));
    }

    [Fact]
    public async Task Service_notes_are_created_once_and_hidden_from_ordinary_lists()
    {
        var (work, notes, _, _) = Build();
        var subjectId = await SeedSubjectAsync("Физика");
        var deadlineId = await SeedDeadlineAsync(subjectId);

        var first = await work.EnsureNotesAsync(deadlineId);
        var second = await work.EnsureNotesAsync(deadlineId);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, second.Value);

        var task = (await notes.GetByIdAsync(first.Value.TaskNoteId))!;
        Assert.Equal(NoteKind.DeadlineTask, task.Kind);
        Assert.Equal(deadlineId, task.DeadlineId);
        Assert.Equal(subjectId, task.SubjectId);
        Assert.Equal(NoteKind.DeadlineAnswer, (await notes.GetByIdAsync(first.Value.AnswerNoteId))!.Kind);

        // Обычные списки заметок служебных не видят.
        var ordinary = (await notes.CreateAsync(new Note { SubjectId = subjectId, Title = "Конспект", ContentMarkdown = "x" })).Value;
        Assert.Equal([ordinary], (await notes.GetBySubjectAsync(subjectId)).Select(x => x.Id));
        Assert.Equal([ordinary], (await notes.GetRecentAsync(10)).Select(x => x.Id));
    }

    [Fact]
    public async Task Note_storage_points_into_the_deadline_folder_and_export_writes_task_md()
    {
        var (work, notes, _, _) = Build();
        var subjectId = await SeedSubjectAsync("Физика");
        var deadlineId = await SeedDeadlineAsync(subjectId, "ЛР 3");
        var pair = (await work.EnsureNotesAsync(deadlineId)).Value;
        var folder = (await work.GetFolderPathAsync(deadlineId))!;

        var storage = (await notes.GetStorageAsync(pair.TaskNoteId))!;
        Assert.Equal(folder, storage.Directory);
        Assert.Equal("Задание.md", storage.ExportFileName);
        Assert.Equal(Path.Combine(folder, "Рисунки"), storage.ImagesDirectory);
        Assert.Equal("Ответ.md", (await notes.GetStorageAsync(pair.AnswerNoteId))!.ExportFileName);

        await notes.UpdateContentAsync(pair.TaskNoteId, "Задание", "Собрать RC-фильтр");
        var exported = await notes.ExportMarkdownAsync(pair.TaskNoteId);

        Assert.True(exported.IsSuccess);
        Assert.Equal(Path.Combine(folder, "Задание.md"), exported.Value);
        Assert.Contains("Собрать RC-фильтр", await File.ReadAllTextAsync(exported.Value));

        // Обычная заметка того же предмета живёт по-старому — в Заметки/ и Рисунки/ предмета.
        var ordinary = (await notes.CreateAsync(new Note { SubjectId = subjectId, Title = "Конспект", ContentMarkdown = "x" })).Value;
        var ordinaryStorage = (await notes.GetStorageAsync(ordinary))!;
        Assert.Equal(Path.Combine(_root, "Физика", "Заметки"), ordinaryStorage.Directory);
        Assert.Equal(Path.Combine(_root, "Физика", "Рисунки"), ordinaryStorage.ImagesDirectory);
    }

    [Fact]
    public async Task Copying_attachments_keeps_the_source_and_registers_them_by_role()
    {
        var (work, _, attachments, _) = Build();
        var subjectId = await SeedSubjectAsync("Физика");
        var deadlineId = await SeedDeadlineAsync(subjectId, "ЛР 3");
        var methodical = WriteInboxFile("методичка.pdf", "pdf");
        var answer = WriteInboxFile("отчёт.docx", "docx");
        var before = Snapshot(_inbox);

        var task = await work.AddAttachmentsAsync(deadlineId, DeadlineAttachmentRole.Task, [methodical], ImportMode.Copy);
        var reply = await work.AddAttachmentsAsync(deadlineId, DeadlineAttachmentRole.Answer, [answer], ImportMode.Copy);

        Assert.True(task.IsSuccess);
        Assert.True(reply.IsSuccess);
        Assert.Equal(1, task.Value.Imported);
        Assert.Equal(before, Snapshot(_inbox));

        var folder = (await work.GetFolderPathAsync(deadlineId))!;
        Assert.True(File.Exists(Path.Combine(folder, "Материалы", "методичка.pdf")));
        Assert.True(File.Exists(Path.Combine(folder, "Ответ", "отчёт.docx")));

        var rows = await work.GetAttachmentsAsync(deadlineId);
        Assert.Equal(2, rows.Count);
        var material = Assert.Single(rows, x => x.Attachment.Role == DeadlineAttachmentRole.Task);
        Assert.Equal("методичка.pdf", material.Attachment.FileName);
        Assert.True(material.Exists);
        Assert.Equal(Path.Combine(folder, "Материалы", "методичка.pdf"), material.AbsolutePath);
        Assert.False(Path.IsPathRooted(material.Attachment.RelativePath));

        Assert.Equal(2, (await attachments.CountsByDeadlineAsync())[deadlineId]);
        Assert.Equal(2, (await work.GetAttachmentCountsAsync())[deadlineId]);
    }

    [Fact]
    public async Task Moving_attachments_takes_the_file_out_of_the_source()
    {
        var (work, _, _, _) = Build();
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId);
        var source = WriteInboxFile("условие.txt");

        var result = await work.AddAttachmentsAsync(deadlineId, DeadlineAttachmentRole.Task, [source], ImportMode.Move);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine((await work.GetFolderPathAsync(deadlineId))!, "Материалы", "условие.txt")));
    }

    [Fact]
    public async Task Same_name_different_content_keeps_both_files()
    {
        var (work, _, _, _) = Build();
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId);
        var first = WriteInboxFile("v1.txt", "первая версия");
        await work.AddAttachmentsAsync(deadlineId, DeadlineAttachmentRole.Answer, [first], ImportMode.Copy);
        File.WriteAllText(first, "вторая версия");

        var second = await work.AddAttachmentsAsync(deadlineId, DeadlineAttachmentRole.Answer, [first], ImportMode.Copy);

        Assert.True(second.IsSuccess);
        var answerFolder = Path.Combine((await work.GetFolderPathAsync(deadlineId))!, "Ответ");
        Assert.Equal(2, Directory.GetFiles(answerFolder).Length);
        Assert.Equal(2, (await work.GetAttachmentsAsync(deadlineId)).Count);
    }

    [Fact]
    public async Task Removing_an_attachment_drops_only_the_record_and_never_the_file()
    {
        var (work, _, _, _) = Build();
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId);
        await work.AddAttachmentsAsync(deadlineId, DeadlineAttachmentRole.Task, [WriteInboxFile("а.txt")], ImportMode.Copy);
        var folder = (await work.GetFolderPathAsync(deadlineId))!;
        var before = Snapshot(folder);
        var row = Assert.Single(await work.GetAttachmentsAsync(deadlineId));

        var removed = await work.RemoveAttachmentAsync(row.Attachment.Id);

        Assert.True(removed.IsSuccess);
        Assert.Empty(await work.GetAttachmentsAsync(deadlineId));
        Assert.Equal(before, Snapshot(folder));
        Assert.True((await work.RemoveAttachmentAsync(row.Attachment.Id)).IsFailure);
    }

    [Fact]
    public async Task Submit_sets_the_answer_date_and_closes_the_deadline_reopen_reverts_both()
    {
        var (work, _, _, _) = Build();
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId);

        Assert.True((await work.SubmitAsync(deadlineId)).IsSuccess);
        var submitted = (await DeadlineRepo.GetByIdAsync(deadlineId))!;
        Assert.NotNull(submitted.AnsweredAt);
        Assert.Equal(DeadlineStatus.Done, submitted.Status);

        Assert.True((await work.ReopenAsync(deadlineId)).IsSuccess);
        var reopened = (await DeadlineRepo.GetByIdAsync(deadlineId))!;
        Assert.Null(reopened.AnsweredAt);
        Assert.Equal(DeadlineStatus.Pending, reopened.Status);

        Assert.True((await work.SubmitAsync(Guid.NewGuid())).IsFailure);
    }

    [Fact]
    public async Task Deleting_the_deadline_removes_its_notes_and_records_but_leaves_files_on_disk()
    {
        var (work, notes, _, _) = Build();
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId);
        var pair = (await work.EnsureNotesAsync(deadlineId)).Value;
        await work.AddAttachmentsAsync(deadlineId, DeadlineAttachmentRole.Task, [WriteInboxFile("а.txt")], ImportMode.Copy);
        var folder = (await work.GetFolderPathAsync(deadlineId))!;
        var before = Snapshot(folder);
        var ordinary = (await notes.CreateAsync(new Note { SubjectId = subjectId, Title = "Конспект", ContentMarkdown = "x" })).Value;

        Assert.True((await Deadlines.DeleteAsync(deadlineId)).IsSuccess);

        Assert.Null(await notes.GetByIdAsync(pair.TaskNoteId));
        Assert.Null(await notes.GetByIdAsync(pair.AnswerNoteId));
        Assert.NotNull(await notes.GetByIdAsync(ordinary));
        Assert.Empty(await work.GetAttachmentsAsync(deadlineId));
        Assert.Equal(before, Snapshot(folder));
    }

    /// <summary>Учебная папка как временный каталог; <c>null</c> — учебная папка не настроена.</summary>
    internal sealed class Workspace(string? root) : IStudyWorkspace
    {
        public string StudyRootPath => root ?? string.Empty;
        public bool HasStudyRoot => root is not null && Directory.Exists(root);
        public string GetSubjectDirectory(Subject subject) => root is null ? string.Empty : SubjectFolder.Resolve(root, subject);
        public void EnsureSubjectScaffold(Subject subject) => Directory.CreateDirectory(GetSubjectDirectory(subject));
        public IReadOnlyList<string> EnumerateSubjectFiles(Subject subject, string? subPath = null) => [];
        public string? ResolveRelative(string absolutePath) => root is null ? null : Path.GetRelativePath(root, absolutePath);
    }
}
