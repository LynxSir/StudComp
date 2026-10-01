using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.FileSystem;
using StudComp.Modules.Organizer.Services;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Двухэтапная корзина картинок заметки на настоящей файловой системе. Главный инвариант: файл
/// либо на месте, либо в корзине — «исчез бесследно» не должно получаться ни в одном исходе
/// (ARCHITECTURE §14), поэтому содержимое папок проверяется явно.
/// </summary>
public sealed class NoteImageTrashTests : OrganizerDatabaseTestBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rubrica-note-trash-" + Guid.NewGuid().ToString("N"));

    public NoteImageTrashTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Корзина, которая ничего не уничтожает молча: запоминает, что ей отдали.</summary>
    private sealed class FakeRecycleBin(bool accepts = true) : IRecycleBinPort
    {
        public List<string> Sent { get; } = [];

        public bool TrySendToRecycleBin(string path)
        {
            if (!accepts)
            {
                return false;
            }

            Sent.Add(path);
            File.Delete(path);
            return true;
        }

        public bool TryRemoveEmptyDirectory(string path)
        {
            try
            {
                Directory.Delete(path, recursive: false);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private (INoteImageTrash Trash, FakeRecycleBin Bin) Build(bool binAccepts = true)
    {
        var workspace = new DeadlineWorkServiceTests.Workspace(_root);
        var notes = new NoteService(NoteRepo, SubjectRepo, workspace);
        var bin = new FakeRecycleBin(binAccepts);
        var trash = new NoteImageTrash(
            notes, workspace, new SystemFileSystem(), bin, NullLogger<NoteImageTrash>.Instance);
        return (trash, bin);
    }

    /// <summary>Заметка предмета плюс картинка в её папке «Рисунки». Отдаёт путь от учебной папки.</summary>
    private async Task<(Guid NoteId, string Relative, string Absolute)> SeedImageAsync(
        string fileName = "Рисунок_1.png", string content = "картинка")
    {
        var subjectId = await SeedSubjectAsync("Математика");
        var subject = await SubjectRepo.GetByIdAsync(subjectId);
        var images = Path.Combine(
            new DeadlineWorkServiceTests.Workspace(_root).GetSubjectDirectory(subject!), "Рисунки");
        Directory.CreateDirectory(images);

        var absolute = Path.Combine(images, fileName);
        File.WriteAllText(absolute, content);

        var created = await Notes.CreateAsync(new Note
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            Kind = NoteKind.Free,
            Title = "Конспект",
            ContentMarkdown = string.Empty,
        });
        Assert.True(created.IsSuccess);

        return (created.Value, Path.GetRelativePath(_root, absolute), absolute);
    }

    private string[] Staged() =>
        Directory.Exists(NoteImageTrashLayout.TrashRoot(_root))
            ? Directory.GetFiles(NoteImageTrashLayout.TrashRoot(_root), "*", SearchOption.AllDirectories)
            : [];

    [Fact]
    public async Task Staging_moves_the_file_out_of_the_subject_folder()
    {
        var (trash, _) = Build();
        var (noteId, relative, absolute) = await SeedImageAsync();

        Assert.True(await trash.StageAsync(noteId, relative));

        Assert.False(File.Exists(absolute));
        // Файл не исчез — он лежит в скрытой корзине учебной папки.
        Assert.Equal("картинка", File.ReadAllText(Assert.Single(Staged())));
    }

    [Fact]
    public async Task A_staged_file_comes_back_byte_for_byte()
    {
        var (trash, _) = Build();
        var (noteId, relative, absolute) = await SeedImageAsync();
        var original = File.ReadAllBytes(absolute);

        Assert.True(await trash.StageAsync(noteId, relative));
        Assert.True(await trash.RestoreAsync(noteId, relative));

        Assert.Equal(original, File.ReadAllBytes(absolute));
        Assert.Empty(Staged());
    }

    [Fact]
    public async Task The_drawing_sidecar_travels_with_the_image()
    {
        var (trash, _) = Build();
        var (noteId, relative, absolute) = await SeedImageAsync();
        var sidecar = Path.ChangeExtension(absolute, ".json");
        File.WriteAllText(sidecar, "{}");

        Assert.True(await trash.StageAsync(noteId, relative));
        Assert.False(File.Exists(sidecar));

        Assert.True(await trash.RestoreAsync(noteId, relative));
        // Без векторной копии восстановленный рисунок молча потерял бы возможность правки.
        Assert.True(File.Exists(sidecar));
    }

    [Fact]
    public async Task Staging_twice_is_harmless()
    {
        var (trash, _) = Build();
        var (noteId, relative, _) = await SeedImageAsync();

        Assert.True(await trash.StageAsync(noteId, relative));
        Assert.False(await trash.StageAsync(noteId, relative));

        Assert.Single(Staged());
    }

    [Fact]
    public async Task A_file_outside_the_note_images_folder_is_never_touched()
    {
        var (trash, _) = Build();
        var (noteId, _, _) = await SeedImageAsync();

        var stranger = Path.Combine(_root, "Чужое.png");
        File.WriteAllText(stranger, "не наше");

        Assert.False(await trash.StageAsync(noteId, "Чужое.png"));
        Assert.True(File.Exists(stranger));
    }

    [Fact]
    public async Task Commit_sends_everything_left_to_the_recycle_bin()
    {
        var (trash, bin) = Build();
        var (noteId, relative, _) = await SeedImageAsync();

        await trash.StageAsync(noteId, relative);
        Assert.Equal(1, await trash.CommitAsync(noteId));

        Assert.Single(bin.Sent);
        Assert.False(Directory.Exists(NoteImageTrashLayout.NoteDirectory(_root, noteId)));
    }

    [Fact]
    public async Task A_refused_recycle_bin_leaves_the_file_staged()
    {
        var (trash, bin) = Build(binAccepts: false);
        var (noteId, relative, _) = await SeedImageAsync();

        await trash.StageAsync(noteId, relative);
        Assert.Equal(0, await trash.CommitAsync(noteId));

        // Отказ означает «файл остался», никогда «файл удалён».
        Assert.Empty(bin.Sent);
        Assert.Single(Staged());
    }

    [Fact]
    public async Task Only_images_the_text_still_references_are_restored()
    {
        var (trash, _) = Build();
        var (noteId, keptRelative, keptAbsolute) = await SeedImageAsync("Нужная.png");

        var droppedAbsolute = Path.Combine(Path.GetDirectoryName(keptAbsolute)!, "Лишняя.png");
        File.WriteAllText(droppedAbsolute, "лишняя");
        var droppedRelative = Path.GetRelativePath(_root, droppedAbsolute);

        await trash.StageAsync(noteId, keptRelative);
        await trash.StageAsync(noteId, droppedRelative);

        var markdown = "![a](<" + MarkdownLocalImages.Encode(keptRelative) + ">)";
        Assert.Equal(1, await trash.RestoreReferencedAsync(noteId, markdown));

        Assert.True(File.Exists(keptAbsolute));
        Assert.False(File.Exists(droppedAbsolute));
    }

    [Fact]
    public async Task The_sweep_only_takes_sessions_older_than_the_threshold()
    {
        var (trash, bin) = Build();
        var (noteId, relative, _) = await SeedImageAsync();

        await trash.StageAsync(noteId, relative);

        Assert.Equal(0, await trash.SweepOrphansAsync(TimeSpan.FromDays(1)));
        Assert.Empty(bin.Sent);

        Assert.Equal(1, await trash.SweepOrphansAsync(TimeSpan.Zero));
        Assert.Single(bin.Sent);
    }

    [Fact]
    public async Task Nothing_happens_without_a_study_folder()
    {
        var workspace = new DeadlineWorkServiceTests.Workspace(null);
        var bin = new FakeRecycleBin();
        var trash = new NoteImageTrash(
            new NoteService(NoteRepo, SubjectRepo, workspace),
            workspace,
            new SystemFileSystem(),
            bin,
            NullLogger<NoteImageTrash>.Instance);

        Assert.False(await trash.StageAsync(Guid.NewGuid(), "a.png"));
        Assert.Equal(0, await trash.CommitAsync(Guid.NewGuid()));
        Assert.Equal(0, await trash.SweepOrphansAsync(TimeSpan.Zero));
        Assert.Empty(bin.Sent);
    }
}
