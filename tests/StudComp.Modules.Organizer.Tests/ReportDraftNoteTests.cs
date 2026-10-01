using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Modules.Organizer.Services;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Черновик текста отчёта – служебная заметка (<see cref="NoteKind.ReportDraft"/>), по одной на
/// предмет. Проверяется на настоящем temp-SQLite и настоящей временной учебной папке.
/// </summary>
/// <remarks>
/// Главный инвариант здесь – файловый: у черновика СВОЯ папка картинок. Корзина картинок трогает
/// только файлы внутри папки картинок своей заметки, поэтому, дели черновик <c>Рисунки</c> с
/// заметками предмета, замена его текста (например, после «Собрать из заметок») увела бы картинки
/// самих заметок в корзину. Это зона ARCHITECTURE §14, поэтому сравниваются снимки папок до и после.
/// </remarks>
public sealed class ReportDraftNoteTests : OrganizerDatabaseTestBase, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "rubrica-report-draft-" + Guid.NewGuid().ToString("N"));

    public ReportDraftNoteTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private INoteService Build() =>
        new NoteService(NoteRepo, SubjectRepo, new DeadlineWorkServiceTests.Workspace(_root), DeadlineRepo);

    private static string[] Snapshot(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order()]
            : [];

    [Fact]
    public async Task Draft_is_created_once_and_reused()
    {
        var notes = Build();
        var subjectId = await SeedSubjectAsync("Физика");

        var first = await notes.EnsureReportDraftAsync(subjectId);
        var second = await notes.EnsureReportDraftAsync(subjectId);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value, second.Value);

        var draft = await notes.GetByIdAsync(first.Value);
        Assert.Equal(NoteKind.ReportDraft, draft!.Kind);
        Assert.Equal(subjectId, draft.SubjectId);
    }

    [Fact]
    public async Task Every_subject_gets_its_own_draft()
    {
        var notes = Build();
        var physics = await SeedSubjectAsync("Физика");
        var maths = await SeedSubjectAsync("Матан");

        var forPhysics = (await notes.EnsureReportDraftAsync(physics)).Value;
        var forMaths = (await notes.EnsureReportDraftAsync(maths)).Value;
        var withoutSubject = (await notes.EnsureReportDraftAsync(null)).Value;

        Assert.NotEqual(forPhysics, forMaths);
        Assert.NotEqual(forPhysics, withoutSubject);
        Assert.NotEqual(forMaths, withoutSubject);
        Assert.Null((await notes.GetByIdAsync(withoutSubject))!.SubjectId);
    }

    [Fact]
    public async Task Draft_for_an_unknown_subject_is_rejected()
    {
        var result = await Build().EnsureReportDraftAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Draft_is_hidden_from_the_note_lists()
    {
        var notes = Build();
        var subjectId = await SeedSubjectAsync("Физика");

        var ordinary = (await notes.CreateAsync(new Note
        {
            SubjectId = subjectId,
            Kind = NoteKind.Lecture,
            Title = "Лекция 1",
            ContentMarkdown = "Текст",
        })).Value;

        await notes.EnsureReportDraftAsync(subjectId);

        // Иначе черновик мелькал бы и в «Заметках» Хаба, и на Дашборде, и в «Собрать из заметок».
        Assert.Equal([ordinary], (await notes.GetBySubjectAsync(subjectId)).Select(note => note.Id));
        Assert.Equal([ordinary], (await notes.GetRecentAsync(10)).Select(note => note.Id));
    }

    [Fact]
    public async Task Draft_stores_its_images_apart_from_the_notes_of_the_same_subject()
    {
        var notes = Build();
        var subjectId = await SeedSubjectAsync("Физика");

        var ordinary = (await notes.CreateAsync(new Note
        {
            SubjectId = subjectId,
            Kind = NoteKind.Lecture,
            Title = "Лекция 1",
            ContentMarkdown = "Текст",
        })).Value;

        var draftId = (await notes.EnsureReportDraftAsync(subjectId)).Value;

        var noteStorage = await notes.GetStorageAsync(ordinary);
        var draftStorage = await notes.GetStorageAsync(draftId);

        Assert.NotNull(noteStorage);
        Assert.NotNull(draftStorage);

        Assert.Equal(Path.Combine(_root, "Физика", "Рисунки"), noteStorage!.ImagesDirectory);
        Assert.Equal(Path.Combine(_root, "Физика", "Отчёты", "Рисунки"), draftStorage!.ImagesDirectory);
        Assert.NotEqual(noteStorage.ImagesDirectory, draftStorage.ImagesDirectory);
    }

    [Fact]
    public async Task Draft_without_a_subject_lands_next_to_the_study_root_reports()
    {
        var notes = Build();
        var draftId = (await notes.EnsureReportDraftAsync(null)).Value;

        var storage = await notes.GetStorageAsync(draftId);

        Assert.Equal(Path.Combine(_root, "Отчёты"), storage!.Directory);
        Assert.Equal(Path.Combine(_root, "Отчёты", "Рисунки"), storage.ImagesDirectory);
    }

    [Fact]
    public async Task Draft_does_not_leave_a_markdown_copy_on_disk()
    {
        var notes = Build();
        var subjectId = await SeedSubjectAsync("Физика");
        var draftId = (await notes.EnsureReportDraftAsync(subjectId)).Value;

        await notes.UpdateContentAsync(draftId, "Черновик отчёта", "# Введение\n\nТекст.");

        // Готовый вид черновика – сгенерированный .docx; второй файл рядом только путал бы.
        var exported = await notes.ExportMarkdownAsync(draftId);
        Assert.True(exported.IsFailure);
        Assert.Equal("note.no_copy", exported.Error.Code);
        Assert.Empty(Snapshot(Path.Combine(_root, "Физика", "Отчёты")));
    }

    [Fact]
    public async Task Replacing_the_draft_text_leaves_the_note_images_of_the_subject_untouched()
    {
        var notes = Build();
        var trash = new NoteImageTrash(
            notes,
            new DeadlineWorkServiceTests.Workspace(_root),
            new SystemFileSystem(),
            new ThrowingRecycleBin(),
            NullLogger<NoteImageTrash>.Instance);

        var subjectId = await SeedSubjectAsync("Физика");

        // Картинка заметки лежит в «Рисунки» предмета.
        var noteImages = Path.Combine(_root, "Физика", "Рисунки");
        Directory.CreateDirectory(noteImages);
        var noteImage = Path.Combine(noteImages, "схема.png");
        await File.WriteAllTextAsync(noteImage, "png");

        var relative = Path.Combine("Физика", "Рисунки", "схема.png");

        await notes.CreateAsync(new Note
        {
            SubjectId = subjectId,
            Kind = NoteKind.Lecture,
            Title = "Лекция 1",
            ContentMarkdown = $"![Схема](<{relative}>)",
        });

        var draftId = (await notes.EnsureReportDraftAsync(subjectId)).Value;
        await notes.UpdateContentAsync(draftId, "Черновик отчёта", $"![Схема](<{relative}>)");

        var before = Snapshot(noteImages);

        // Текст черновика ссылался на картинку заметки, и ссылка ушла. Корзина обязана отказать:
        // файл не принадлежит папке картинок черновика.
        var staged = await trash.StageAsync(draftId, relative);

        Assert.False(staged);
        Assert.Equal(before, Snapshot(noteImages));
        Assert.True(File.Exists(noteImage));

        // Контроль: сам механизм жив – своя картинка черновика в корзину уходит. Без этой проверки
        // «false» выше мог бы означать просто ранний выход, а не отказ именно по принадлежности.
        var draftImages = Path.Combine(_root, "Физика", "Отчёты", "Рисунки");
        Directory.CreateDirectory(draftImages);
        var ownImage = Path.Combine(draftImages, "своя.png");
        await File.WriteAllTextAsync(ownImage, "png");

        var ownRelative = Path.Combine("Физика", "Отчёты", "Рисунки", "своя.png");
        Assert.True(await trash.StageAsync(draftId, ownRelative));
        Assert.False(File.Exists(ownImage));
        Assert.True(await trash.RestoreAsync(draftId, ownRelative));
        Assert.True(File.Exists(ownImage));
    }

    /// <summary>
    /// «Корзина», которая обязана не пригодиться: если сюда дошли, значит черновик тронул чужой
    /// файл – тест обязан упасть громко, а не тихо удалить картинку заметки.
    /// </summary>
    private sealed class ThrowingRecycleBin : StudComp.Core.Abstractions.Workspace.IRecycleBinPort
    {
        public bool TrySendToRecycleBin(string path) =>
            throw new InvalidOperationException($"Черновик не должен трогать чужой файл: {path}");

        public bool TryRemoveEmptyDirectory(string path) => true;
    }
}
