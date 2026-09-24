using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Куда Архивариус кладёт файл предмета (new_addons.md §4). Одна реализация на движок правил и на
/// ручную сортировку «Неразобранного» — до Phase 13.2 их было две, с разной санитизацией имени.
/// </summary>
/// <remarks>
/// Порядок приоритетов (решение владельца Phase 13.2 «свести корень архива к учебной папке»):
/// <list type="number">
///   <item>своя папка предмета (абсолютный путь) — сильнее любых корней;</item>
///   <item>непустой <c>ArchiveRootFolder</c> — явное переопределение: у кого архив исторически лежит
///     отдельно, файлы продолжают ехать туда же, куда ехали (менять это молча — значит увести
///     пользовательские файлы в другое место, чего §14 не допускает);</item>
///   <item>иначе — учебная папка, ровно тот же путь, что показывает Хаб предмета;</item>
///   <item>подставить нечего — <see langword="null"/>, файл остаётся в «Неразобранном», а не теряется.</item>
/// </list>
/// </remarks>
internal sealed class SubjectTargetResolver(
    IStudyWorkspace workspace,
    IOptionsMonitor<ArchivistOptions> options)
{
    /// <summary>Целевая папка; <see langword="null"/> — цель не определена.</summary>
    public string? Resolve(Subject? subject)
    {
        var archiveRoot = options.CurrentValue.ArchiveRootFolder?.Trim() ?? string.Empty;

        if (subject is null)
        {
            // Правило без предмета кладёт в сам корень: явно заданный архив, иначе учебная папка.
            if (archiveRoot.Length > 0)
            {
                return archiveRoot;
            }

            var root = workspace.StudyRootPath?.Trim() ?? string.Empty;
            return root.Length > 0 ? root : null;
        }

        if (SubjectFolder.IsCustomPath(subject.FolderPath))
        {
            return subject.FolderPath.Trim();
        }

        if (archiveRoot.Length > 0)
        {
            return Path.Combine(archiveRoot, SubjectFolder.FolderName(subject));
        }

        var directory = workspace.GetSubjectDirectory(subject);
        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }
}
