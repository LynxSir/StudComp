using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Реализация live-preview правил (ARCHITECTURE §8.7). Гоняет тот же <see cref="ISortingRuleEngine"/>,
/// что и боевой конвейер, но никогда не вызывает исполнителя операций — на диск только смотрит.
/// </summary>
internal sealed class RulePreviewService(
    IFileWatcherService watcher,
    IArchivistRuleService rules,
    ISortingRuleEngine engine,
    IFileSystem fileSystem,
    IOptionsMonitor<ArchivistOptions> options,
    ILogger<RulePreviewService> logger) : IRulePreviewService
{
    // Верхняя граница на объём выборки — предпросмотр не должен «задумываться» на большой папке.
    private const int MaxFiles = 500;

    public async Task<IReadOnlyList<RulePreviewItem>> PreviewAsync(
        ArchivistRule draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // У правила, привязанного к одной папке, и смотреть смысл только в ней (ADR §16.54).
        var folders = string.IsNullOrWhiteSpace(draft.WatchedFolder)
            ? watcher.WatchedFolders
            : [PathComparison.Normalize(draft.WatchedFolder)];

        var current = options.CurrentValue;
        var soloRule = Clone(draft);
        var higherPriorityRules = (await rules.GetActiveAsync(ct).ConfigureAwait(false))
            .Where(r => r.Id != draft.Id)
            .ToList();

        var items = new List<RulePreviewItem>();
        var budget = MaxFiles;

        foreach (var folder in folders)
        {
            if (budget <= 0)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(folder) || !fileSystem.DirectoryExists(folder))
            {
                continue;
            }

            List<string> files;
            try
            {
                files = [.. fileSystem.EnumerateFiles(folder).Take(budget)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Предпросмотр: не удалось прочитать папку {Folder}", folder);
                continue;
            }

            budget -= files.Count;

            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(path);
                if (IgnoreListMatcher.IsIgnoredName(fileName, current.IgnoredPatterns))
                {
                    continue;
                }

                FileAttributes attributes;
                long size;
                DateTimeOffset createdUtc;
                DateTimeOffset modifiedUtc;
                try
                {
                    attributes = fileSystem.GetAttributes(path);
                    size = fileSystem.GetFileSize(path);
                    createdUtc = fileSystem.GetCreationTimeUtc(path);
                    modifiedUtc = fileSystem.GetLastWriteTimeUtc(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (IgnoreListMatcher.IsSystemOrHidden(attributes)
                    || (current.SkipCloudPlaceholders && IgnoreListMatcher.IsCloudPlaceholder(attributes)))
                {
                    continue;
                }

                var info = new WatchedFileInfo(
                    FullPath: path,
                    FileName: fileName,
                    Extension: Path.GetExtension(fileName).ToLowerInvariant(),
                    SizeBytes: size,
                    CreatedAtUtc: createdUtc,
                    ModifiedAtUtc: modifiedUtc,
                    WatchedFolderPath: folder);

                var decision = await engine.EvaluateAsync(info, [soloRule], ct).ConfigureAwait(false);
                if (decision is null)
                {
                    continue;
                }

                var withHigher = higherPriorityRules.Count == 0
                    ? null
                    : await engine.EvaluateAsync(info, higherPriorityRules, ct).ConfigureAwait(false);

                items.Add(new RulePreviewItem(
                    FileName: fileName,
                    CurrentPath: path,
                    ProjectedName: decision.NewFileName,
                    ProjectedTargetDirectory: decision.TargetDirectory,
                    AlreadyHandledByOtherRule: withHigher is not null));
            }
        }

        return items;
    }

    private static ArchivistRule Clone(ArchivistRule source) => new()
    {
        Id = source.Id,
        SubjectId = source.SubjectId,
        Pattern = source.Pattern,
        MatchType = source.MatchType,
        Priority = source.Priority,
        Enabled = true,
        WorkType = source.WorkType,
        WatchedFolder = source.WatchedFolder,
        RenameTemplate = source.RenameTemplate,
    };
}
