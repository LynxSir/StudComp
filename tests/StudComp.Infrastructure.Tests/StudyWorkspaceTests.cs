using StudComp.Core.Domain;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;
using StudComp.Infrastructure.Workspace;

namespace StudComp.Infrastructure.Tests;

/// <summary>
/// Тесты <see cref="StudyWorkspace"/> на реальных temp-каталогах (Phase 12.1). Учебная папка — единая
/// точка правды по путям предметов для трёх модулей.
/// </summary>
public sealed class StudyWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rubrica-ws-" + Guid.NewGuid().ToString("N"));

    private StudyWorkspace Create(WorkspaceOptions options) =>
        new(new TestOptionsMonitor<WorkspaceOptions>(options), new SystemFileSystem());

    private static Subject SubjectNamed(string name, string folderPath = "") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        FolderPath = folderPath,
    };

    [Fact]
    public void GetSubjectDirectory_prefers_explicit_folder_path()
    {
        var ws = Create(new WorkspaceOptions { StudyRootPath = _root });

        var dir = ws.GetSubjectDirectory(SubjectNamed("Матан", @"D:\Своя\Папка"));

        Assert.Equal(@"D:\Своя\Папка", dir);
    }

    [Fact]
    public void GetSubjectDirectory_computes_from_root_and_sanitized_name()
    {
        var ws = Create(new WorkspaceOptions { StudyRootPath = _root });

        var dir = ws.GetSubjectDirectory(SubjectNamed("Мат/Анализ: 1"));

        Assert.Equal(Path.Combine(_root, "Мат_Анализ_ 1"), dir);
    }

    [Fact]
    public void GetSubjectDirectory_treats_a_relative_folder_path_as_a_name_inside_the_root()
    {
        // Phase 13.2: обычный случай — в форме предмета указано только имя подпапки.
        var ws = Create(new WorkspaceOptions { StudyRootPath = _root });

        var dir = ws.GetSubjectDirectory(SubjectNamed("Математический анализ", "МА-2"));

        Assert.Equal(Path.Combine(_root, "МА-2"), dir);
    }

    [Fact]
    public void GetSubjectDirectory_is_empty_for_a_relative_name_without_a_root()
    {
        var ws = Create(new WorkspaceOptions { StudyRootPath = "" });

        Assert.Equal(string.Empty, ws.GetSubjectDirectory(SubjectNamed("Матан", "МА-2")));
    }

    [Fact]
    public void GetSubjectDirectory_is_empty_without_root_or_folder_path()
    {
        var ws = Create(new WorkspaceOptions { StudyRootPath = "" });

        Assert.Equal(string.Empty, ws.GetSubjectDirectory(SubjectNamed("Матан")));
    }

    [Fact]
    public void HasStudyRoot_reflects_the_directory_on_disk()
    {
        Assert.False(Create(new WorkspaceOptions { StudyRootPath = _root }).HasStudyRoot);

        Directory.CreateDirectory(_root);
        Assert.True(Create(new WorkspaceOptions { StudyRootPath = _root }).HasStudyRoot);
    }

    [Fact]
    public void EnsureSubjectScaffold_creates_the_subject_folder_and_template_subfolders()
    {
        Directory.CreateDirectory(_root);
        var ws = Create(new WorkspaceOptions
        {
            StudyRootPath = _root,
            SubjectFolderTemplate = ["Лекции", "Отчёты"],
        });
        var subject = SubjectNamed("Физика");

        ws.EnsureSubjectScaffold(subject);

        var dir = Path.Combine(_root, "Физика");
        Assert.True(Directory.Exists(dir));
        Assert.True(Directory.Exists(Path.Combine(dir, "Лекции")));
        Assert.True(Directory.Exists(Path.Combine(dir, "Отчёты")));
    }

    [Fact]
    public void EnsureSubjectScaffold_is_noop_without_root()
    {
        var ws = Create(new WorkspaceOptions { StudyRootPath = "" });

        var ex = Record.Exception(() => ws.EnsureSubjectScaffold(SubjectNamed("Химия")));

        Assert.Null(ex);
    }

    [Fact]
    public void EnumerateSubjectFiles_lists_files_and_returns_empty_when_missing()
    {
        Directory.CreateDirectory(_root);
        var ws = Create(new WorkspaceOptions { StudyRootPath = _root });
        var subject = SubjectNamed("История");

        Assert.Empty(ws.EnumerateSubjectFiles(subject));

        var dir = Path.Combine(_root, "История");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "конспект.md"), "x");

        var files = ws.EnumerateSubjectFiles(subject);
        Assert.Single(files);
        Assert.EndsWith("конспект.md", files[0]);
    }

    [Fact]
    public void ResolveRelative_returns_path_inside_and_null_outside()
    {
        Directory.CreateDirectory(_root);
        var ws = Create(new WorkspaceOptions { StudyRootPath = _root });

        Assert.Equal(
            Path.Combine("Матан", "лр1.docx"),
            ws.ResolveRelative(Path.Combine(_root, "Матан", "лр1.docx")));

        Assert.Null(ws.ResolveRelative(Path.Combine(Path.GetTempPath(), "снаружи.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
