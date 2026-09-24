using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;

namespace StudComp.Infrastructure.Workspace;

/// <summary>
/// Реализация <see cref="IStudyWorkspace"/> поверх <see cref="IFileSystem"/> +
/// <see cref="IOptionsMonitor{TOptions}"/> (new_addons.md §1.1). Ничего не удаляет — метода удаления в
/// <see cref="IFileSystem"/> нет по конструкции (§14, ADR §16.30).
/// </summary>
internal sealed class StudyWorkspace(IOptionsMonitor<WorkspaceOptions> options, IFileSystem fileSystem)
    : IStudyWorkspace
{
    public string StudyRootPath => options.CurrentValue.StudyRootPath ?? string.Empty;

    public bool HasStudyRoot =>
        !string.IsNullOrWhiteSpace(StudyRootPath) && fileSystem.DirectoryExists(StudyRootPath);

    public string GetSubjectDirectory(Subject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // Вся трактовка FolderPath (пусто / имя подпапки / своя папка) — в SubjectFolder, чтобы
        // Архивариус и Хаб предмета не могли разойтись в вычислении пути (new_addons.md §4).
        return SubjectFolder.Resolve(StudyRootPath, subject);
    }

    public void EnsureSubjectScaffold(Subject subject)
    {
        var directory = GetSubjectDirectory(subject);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        fileSystem.CreateDirectory(directory);

        foreach (var sub in options.CurrentValue.SubjectFolderTemplate ?? [])
        {
            if (string.IsNullOrWhiteSpace(sub))
            {
                continue;
            }

            fileSystem.CreateDirectory(Path.Combine(directory, SubjectFolder.Sanitize(sub)));
        }
    }

    public IReadOnlyList<string> EnumerateSubjectFiles(Subject subject, string? subPath = null)
    {
        var baseDirectory = GetSubjectDirectory(subject);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return [];
        }

        var target = string.IsNullOrWhiteSpace(subPath)
            ? baseDirectory
            : Path.Combine(baseDirectory, subPath);

        return fileSystem.DirectoryExists(target)
            ? fileSystem.EnumerateFiles(target).ToArray()
            : [];
    }

    public string? ResolveRelative(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(StudyRootPath))
        {
            return null;
        }

        var root = Path.GetFullPath(StudyRootPath);
        var full = Path.GetFullPath(absolutePath);
        var relative = Path.GetRelativePath(root, full);

        // Выход за пределы корня — GetRelativePath вернёт путь с ".." или абсолютный.
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return null;
        }

        return relative == "." ? string.Empty : relative;
    }
}
