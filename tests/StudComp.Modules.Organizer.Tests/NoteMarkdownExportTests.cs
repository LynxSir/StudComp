using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Modules.Organizer.Services;

namespace StudComp.Modules.Organizer.Tests;

public sealed class NoteMarkdownExportTests : OrganizerDatabaseTestBase
{
    [Fact]
    public async Task Export_is_readable_and_image_links_resolve_from_the_note_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "rubrica-note-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new Workspace(root);
            var service = new NoteService(NoteRepo, SubjectRepo, workspace);
            var subjectId = await SeedSubjectAsync("Физика");
            var image = Path.Combine(root, "Физика", "Рисунки", "фото #1.png");
            Directory.CreateDirectory(Path.GetDirectoryName(image)!);
            await File.WriteAllBytesAsync(image, [1, 2, 3]);
            var id = (await service.CreateAsync(new Note
            {
                SubjectId = subjectId, Title = "Конспект", ContentMarkdown = "Текст\n![Фото](<Физика/Рисунки/фото%20%231.png>){width=320}",
            })).Value;
            var exported = await service.ExportMarkdownAsync(id);
            Assert.True(exported.IsSuccess);
            var text = await File.ReadAllTextAsync(exported.Value);
            Assert.StartsWith("# Конспект", text);
            var relative = Assert.Single(MarkdownLocalImages.Paths(text));
            Assert.Equal(image, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exported.Value)!, relative)));
            Assert.Contains("width=320", text);
            await service.UpdateContentAsync(id, "Новое имя", "Новый текст");
            var updated = await service.ExportMarkdownAsync(id);
            Assert.Equal(exported.Value, updated.Value);
            Assert.Contains("Новый текст", await File.ReadAllTextAsync(updated.Value));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(updated.Value)!, "*.md"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Without_a_study_folder_the_database_still_keeps_the_note()
    {
        var note = new Note { Title = "Текст", ContentMarkdown = "Сохранено" };
        var id = (await Notes.CreateAsync(note)).Value;
        Assert.True((await Notes.ExportMarkdownAsync(id)).IsFailure);
        Assert.Equal("Сохранено", (await Notes.GetByIdAsync(id))!.ContentMarkdown);
    }

    private sealed class Workspace(string root) : IStudyWorkspace
    {
        public string StudyRootPath => root;
        public bool HasStudyRoot => Directory.Exists(root);
        public string GetSubjectDirectory(Subject subject) => SubjectFolder.Resolve(root, subject);
        public void EnsureSubjectScaffold(Subject subject) => Directory.CreateDirectory(GetSubjectDirectory(subject));
        public IReadOnlyList<string> EnumerateSubjectFiles(Subject subject, string? subPath = null) => [];
        public string? ResolveRelative(string absolutePath) => Path.GetRelativePath(root, absolutePath);
    }
}
