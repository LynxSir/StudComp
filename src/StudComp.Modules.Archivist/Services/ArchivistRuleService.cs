using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Операции над правилами сортировки (ARCHITECTURE §8.7): валидация + <see cref="Result"/> поверх
/// <see cref="IArchivistRuleRepository"/>. Кроме CRUD — перестановка приоритета (drag-n-drop в UI) и
/// экспорт/импорт набора правил в JSON.
/// </summary>
public interface IArchivistRuleService
{
    Task<IReadOnlyList<ArchivistRule>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Включённые правила по убыванию приоритета - в таком виде их ждёт движок правил.</summary>
    Task<IReadOnlyList<ArchivistRule>> GetActiveAsync(CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(ArchivistRule rule, CancellationToken ct = default);

    Task<Result> UpdateAsync(ArchivistRule rule, CancellationToken ct = default);

    Task<Result> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Переставить правила: <paramref name="orderedIdsHighToLow"/> — весь набор правил в порядке от
    /// высшего приоритета к низшему. Приоритеты перенумеровываются шагом 10.
    /// </summary>
    Task<Result> ReorderAsync(IReadOnlyList<Guid> orderedIdsHighToLow, CancellationToken ct = default);

    /// <summary>Выгрузить все правила в JSON-файл. Возвращает число выгруженных правил.</summary>
    Task<Result<int>> ExportToFileAsync(string path, CancellationToken ct = default);

    /// <summary>Загрузить правила из JSON-файла (заменить весь набор либо добавить к текущему).</summary>
    Task<Result<RulesImportSummary>> ImportFromFileAsync(
        string path, RulesImportMode mode, CancellationToken ct = default);
}

internal sealed class ArchivistRuleService(
    IArchivistRuleRepository rules,
    ISubjectRepository subjects,
    IFileSystem fileSystem) : IArchivistRuleService
{
    public Task<IReadOnlyList<ArchivistRule>> GetAllAsync(CancellationToken ct = default) =>
        rules.GetAllAsync(ct);

    public Task<IReadOnlyList<ArchivistRule>> GetActiveAsync(CancellationToken ct = default) =>
        rules.GetEnabledOrderedByPriorityAsync(ct);

    public async Task<Result<Guid>> CreateAsync(ArchivistRule rule, CancellationToken ct = default)
    {
        Guard.NotNull(rule);

        var validation = await ValidateAsync(rule, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return Result<Guid>.Failure(validation.Error);
        }

        rule.Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
        Normalize(rule);
        await rules.AddAsync(rule, ct).ConfigureAwait(false);
        return Result<Guid>.Success(rule.Id);
    }

    public async Task<Result> UpdateAsync(ArchivistRule rule, CancellationToken ct = default)
    {
        Guard.NotNull(rule);

        var validation = await ValidateAsync(rule, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (await FindAsync(rule.Id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("archivist.rule_not_found", "Правило не найдено.");
        }

        Normalize(rule);
        await rules.UpdateAsync(rule, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var rule = await FindAsync(id, ct).ConfigureAwait(false);
        if (rule is null)
        {
            return Result.Failure("archivist.rule_not_found", "Правило не найдено.");
        }

        rule.Enabled = enabled;
        await rules.UpdateAsync(rule, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await FindAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("archivist.rule_not_found", "Правило не найдено.");
        }

        await rules.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> ReorderAsync(
        IReadOnlyList<Guid> orderedIdsHighToLow, CancellationToken ct = default)
    {
        Guard.NotNull(orderedIdsHighToLow);

        var all = await rules.GetAllAsync(ct).ConfigureAwait(false);
        var byId = all.ToDictionary(r => r.Id);

        if (orderedIdsHighToLow.Count != all.Count
            || orderedIdsHighToLow.Any(id => !byId.ContainsKey(id)))
        {
            return Result.Failure(
                "archivist.reorder_mismatch", "Список правил успел измениться — обновите страницу.");
        }

        const int step = 10;
        var priority = orderedIdsHighToLow.Count * step;
        var updated = new List<ArchivistRule>(orderedIdsHighToLow.Count);
        foreach (var id in orderedIdsHighToLow)
        {
            var rule = byId[id];
            rule.Priority = priority;
            priority -= step;
            updated.Add(rule);
        }

        await rules.UpdateManyAsync(updated, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<int>> ExportToFileAsync(string path, CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(path);

        var all = await rules.GetAllAsync(ct).ConfigureAwait(false);
        var subjectNames = (await subjects.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(s => s.Id, s => s.Name);

        var dtos = all
            .Select(r => new RuleExportDto
            {
                Pattern = r.Pattern,
                MatchType = r.MatchType,
                Priority = r.Priority,
                Enabled = r.Enabled,
                WorkType = r.WorkType,
                RenameTemplate = r.RenameTemplate,
                WatchedFolder = r.WatchedFolder,
                SubjectName = r.SubjectId is { } id && subjectNames.TryGetValue(id, out var name)
                    ? name
                    : null,
            })
            .ToList();

        var json = RulesJson.Serialize(dtos);

        try
        {
            await using var stream = fileSystem.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json.AsMemory(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<int>.Failure("archivist.export_failed", $"Не удалось записать файл: {ex.Message}");
        }

        return Result<int>.Success(dtos.Count);
    }

    public async Task<Result<RulesImportSummary>> ImportFromFileAsync(
        string path, RulesImportMode mode, CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(path);

        string json;
        try
        {
            using var stream = fileSystem.OpenRead(path);
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<RulesImportSummary>.Failure(
                "archivist.import_failed", $"Не удалось прочитать файл: {ex.Message}");
        }

        var parsed = RulesJson.Deserialize(json);
        if (parsed.IsFailure)
        {
            return Result<RulesImportSummary>.Failure(parsed.Error);
        }

        var subjectByName = (await subjects.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var warnings = new List<string>();
        var prepared = new List<ArchivistRule>();
        var skipped = 0;

        foreach (var dto in parsed.Value)
        {
            if (string.IsNullOrWhiteSpace(dto.Pattern))
            {
                skipped++;
                warnings.Add("Пропущено правило без условия.");
                continue;
            }

            if (dto.MatchType == RuleMatchType.Regex && !RegexRulePattern.IsValid(dto.Pattern))
            {
                skipped++;
                warnings.Add($"Пропущено regex-правило с ошибкой в выражении: «{dto.Pattern}».");
                continue;
            }

            Guid? subjectId = null;
            if (!string.IsNullOrWhiteSpace(dto.SubjectName))
            {
                if (subjectByName.TryGetValue(dto.SubjectName.Trim(), out var id))
                {
                    subjectId = id;
                }
                else
                {
                    warnings.Add(
                        $"Предмет «{dto.SubjectName}» не найден — правило добавлено без привязки к предмету.");
                }
            }

            prepared.Add(new ArchivistRule
            {
                Id = Guid.NewGuid(),
                SubjectId = subjectId,
                Pattern = dto.Pattern.Trim(),
                MatchType = dto.MatchType,
                Priority = dto.Priority,
                Enabled = dto.Enabled,
                WorkType = string.IsNullOrWhiteSpace(dto.WorkType) ? null : dto.WorkType.Trim(),
                WatchedFolder = string.IsNullOrWhiteSpace(dto.WatchedFolder)
                    ? null
                    : dto.WatchedFolder.Trim(),
                RenameTemplate = dto.RenameTemplate?.Trim() ?? string.Empty,
            });
        }

        if (mode == RulesImportMode.Replace)
        {
            await rules.ReplaceAllAsync(prepared, ct).ConfigureAwait(false);
        }
        else
        {
            await rules.AddManyAsync(prepared, ct).ConfigureAwait(false);
        }

        return Result<RulesImportSummary>.Success(new RulesImportSummary(prepared.Count, skipped, warnings));
    }

    private Task<ArchivistRule?> FindAsync(Guid id, CancellationToken ct) => rules.GetByIdAsync(id, ct);

    private static void Normalize(ArchivistRule rule)
    {
        rule.Pattern = rule.Pattern.Trim();
        rule.WorkType = string.IsNullOrWhiteSpace(rule.WorkType) ? null : rule.WorkType.Trim();
        rule.WatchedFolder = string.IsNullOrWhiteSpace(rule.WatchedFolder)
            ? null
            : PathComparison.Normalize(rule.WatchedFolder);
        rule.RenameTemplate = rule.RenameTemplate?.Trim() ?? string.Empty;
    }

    private async Task<Result> ValidateAsync(ArchivistRule rule, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return Result.Failure("archivist.rule_pattern_required", "Условие правила обязательно.");
        }

        if (rule.MatchType == RuleMatchType.Regex && !RegexRulePattern.IsValid(rule.Pattern))
        {
            return Result.Failure(
                "archivist.regex_invalid", "Регулярное выражение написано с ошибкой — проверьте синтаксис.");
        }

        if (rule.SubjectId is { } subjectId
            && await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("archivist.subject_not_found", "Предмет не найден.");
        }

        return Result.Success();
    }
}
