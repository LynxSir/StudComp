using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Движок правил сортировки (ARCHITECTURE §8.3, §8.4 п.3–4). Чистое принятие решения: файловую систему
/// не трогает совсем, только читает предметы и настройки. Правила перебираются по убыванию приоритета,
/// первое совпадение побеждает; ноль совпадений — <see langword="null"/>, и файл уходит в
/// «Неразобранное», а не теряется.
/// </summary>
internal sealed class SortingRuleEngine(
    ISubjectRepository subjects,
    IOptionsMonitor<ArchivistOptions> options,
    SubjectTargetResolver targets,
    ILogger<SortingRuleEngine> logger) : ISortingRuleEngine
{
    // Компилированные regex кешируются по тексту паттерна: движок — singleton, а один и тот же
    // паттерн проверяется по многу файлов подряд. Значение null помечает «не компилируется» —
    // чтобы не пытаться собрать его заново на каждом файле.
    private readonly ConcurrentDictionary<string, Regex?> _regexCache = new(StringComparer.Ordinal);

    public async Task<SortDecision?> EvaluateAsync(
        WatchedFileInfo file,
        IReadOnlyList<ArchivistRule> rules,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(rules);

        var ordered = rules
            .Where(r => r.Enabled && AppliesToFolder(r, file.WatchedFolderPath))
            .OrderByDescending(r => r.Priority)
            .ToList();

        foreach (var rule in ordered)
        {
            if (!Matches(file, rule))
            {
                continue;
            }

            var subject = rule.SubjectId is { } subjectId
                ? await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false)
                : null;

            if (rule.SubjectId is not null && subject is null)
            {
                logger.LogWarning(
                    "Правило {RuleId} ссылается на несуществующий предмет — пропущено", rule.Id);
                continue;
            }

            var targetDirectory = targets.Resolve(subject);
            if (targetDirectory is null)
            {
                logger.LogInformation(
                    "Правило {RuleId} подошло файлу {File}, но целевая папка не определена "
                    + "(у предмета не задана папка и не задан корень архива) — файл остаётся неразобранным",
                    rule.Id,
                    file.FileName);
                continue;
            }

            var newFileName = RenameTemplate.Apply(
                rule.RenameTemplate,
                new WatchedFileInfoValues(file.FileName, file.Extension),
                rule,
                subject?.Name ?? string.Empty,
                DateTimeOffset.Now);

            return new SortDecision(
                SourceFileRecordId: Guid.Empty,
                SubjectId: subject?.Id,
                TargetDirectory: targetDirectory,
                NewFileName: newFileName,
                MatchedRule: rule,
                Conflict: ConflictResolution.None);
        }

        return null;
    }

    /// <summary>
    /// Область действия правила (ARCHITECTURE §8.7, ADR §16.54): пустая <c>WatchedFolder</c> — правило
    /// работает во всех наблюдаемых папках, заданная — только в своей. Сигнатура §8.3 из-за этого не
    /// меняется: корень наблюдения приезжает в <c>WatchedFileInfo</c> с самого начала.
    /// </summary>
    private static bool AppliesToFolder(ArchivistRule rule, string watchedFolderPath)
    {
        if (string.IsNullOrWhiteSpace(rule.WatchedFolder))
        {
            return true;
        }

        return PathComparison.SameDirectory(rule.WatchedFolder, watchedFolderPath);
    }

    private bool Matches(WatchedFileInfo file, ArchivistRule rule)
    {
        var pattern = rule.Pattern?.Trim();
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        return rule.MatchType switch
        {
            RuleMatchType.Extension => MatchesExtension(file.Extension, pattern),
            RuleMatchType.Keyword => MatchesKeyword(file.FileName, pattern),
            RuleMatchType.Regex => MatchesRegex(file.FileName, pattern),
            _ => false,
        };
    }

    /// <summary>
    /// Regex по полному имени файла с расширением (ARCHITECTURE §8.4 п.3, пример
    /// <c>^ЛР\d+_.*\.docx$</c>). Регистр не важен. Кривой паттерн или превышение таймаута
    /// (защита от катастрофического бэктрекинга) не роняют конвейер — правило просто не срабатывает.
    /// </summary>
    private bool MatchesRegex(string fileName, string pattern)
    {
        var regex = _regexCache.GetOrAdd(pattern, CompileRegex);
        if (regex is null)
        {
            return false;
        }

        try
        {
            return regex.IsMatch(fileName);
        }
        catch (RegexMatchTimeoutException)
        {
            logger.LogWarning(
                "Regex-правило «{Pattern}» превысило таймаут на файле {File} — пропущено", pattern, fileName);
            return false;
        }
    }

    private Regex? CompileRegex(string pattern)
    {
        try
        {
            var timeout = TimeSpan.FromMilliseconds(Math.Max(1, options.CurrentValue.RegexMatchTimeoutMs));
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, timeout);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Regex-правило «{Pattern}» не компилируется — пропущено", pattern);
            return null;
        }
    }

    /// <summary>Терпим все привычные формы записи расширения: <c>docx</c>, <c>.docx</c>, <c>*.docx</c>.</summary>
    private static bool MatchesExtension(string fileExtension, string pattern)
    {
        var normalized = pattern.TrimStart('*');
        if (!normalized.StartsWith('.'))
        {
            normalized = "." + normalized;
        }

        return string.Equals(fileExtension, normalized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Вхождение подстроки в имя файла без расширения. Чтение содержимого <c>.docx</c>/<c>.pdf</c>
    /// (опция из §8.4 п.3) отложено на Phase 8.
    /// </summary>
    private static bool MatchesKeyword(string fileName, string pattern) =>
        Path.GetFileNameWithoutExtension(fileName)
            .Contains(pattern, StringComparison.OrdinalIgnoreCase);
}
