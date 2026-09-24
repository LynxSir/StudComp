using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>CRUD правил и ручная сортировка «Неразобранного» (ARCHITECTURE §8.4 п.7, §8.7).</summary>
public sealed class ArchivistServiceTests : ArchivistDatabaseTestBase, IDisposable
{
    private readonly TempFolder _source = new("svc-src");
    private readonly TempFolder _archive = new("svc-dst");

    private IUnsortedFileService CreateUnsortedService() => new UnsortedFileService(
        FileRecordRepo,
        SubjectRepo,
        Executor,
        FileSystem,
        Targets,
        NullLogger<UnsortedFileService>.Instance);

    [Fact]
    public async Task Rule_without_pattern_is_rejected()
    {
        var result = await Rules.CreateAsync(new ArchivistRule { Pattern = "   ", MatchType = RuleMatchType.Keyword });

        Assert.True(result.IsFailure);
        Assert.Equal("archivist.rule_pattern_required", result.Error.Code);
    }

    [Fact]
    public async Task Valid_regex_rule_is_accepted()
    {
        var result = await Rules.CreateAsync(new ArchivistRule
        {
            Pattern = @"^ЛР\d+_.*\.docx$",
            MatchType = RuleMatchType.Regex,
        });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Invalid_regex_rule_is_rejected()
    {
        var result = await Rules.CreateAsync(new ArchivistRule { Pattern = "([", MatchType = RuleMatchType.Regex });

        Assert.True(result.IsFailure);
        Assert.Equal("archivist.regex_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Reorder_renumbers_priorities_top_to_bottom()
    {
        var a = (await Rules.CreateAsync(new ArchivistRule { Pattern = ".a", MatchType = RuleMatchType.Extension, Priority = 1 })).Value;
        var b = (await Rules.CreateAsync(new ArchivistRule { Pattern = ".b", MatchType = RuleMatchType.Extension, Priority = 2 })).Value;
        var c = (await Rules.CreateAsync(new ArchivistRule { Pattern = ".c", MatchType = RuleMatchType.Extension, Priority = 3 })).Value;

        var result = await Rules.ReorderAsync([c, a, b]);
        Assert.True(result.IsSuccess);

        var all = (await Rules.GetAllAsync()).ToDictionary(r => r.Id, r => r.Priority);
        Assert.True(all[c] > all[a]);
        Assert.True(all[a] > all[b]);
    }

    [Fact]
    public async Task Reorder_with_stale_id_set_is_rejected()
    {
        await Rules.CreateAsync(new ArchivistRule { Pattern = ".a", MatchType = RuleMatchType.Extension });

        var result = await Rules.ReorderAsync([Guid.NewGuid()]);

        Assert.True(result.IsFailure);
        Assert.Equal("archivist.reorder_mismatch", result.Error.Code);
    }

    [Fact]
    public async Task Rules_survive_export_then_import_round_trip()
    {
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = @"^ЛР\d+.*\.docx$",
            MatchType = RuleMatchType.Regex,
            Priority = 30,
            Enabled = true,
            WorkType = "ЛР",
            WatchedFolder = _source.Path,
            RenameTemplate = "{Subject}_{Type}{Ext}",
        });
        await Rules.CreateAsync(new ArchivistRule
        {
            Pattern = ".pdf",
            MatchType = RuleMatchType.Extension,
            Priority = 5,
            Enabled = false,
        });

        var file = _source.Combine("rules.json");
        var exported = await Rules.ExportToFileAsync(file);
        Assert.Equal(2, exported.Value);

        var imported = await Rules.ImportFromFileAsync(file, RulesImportMode.Replace);
        Assert.True(imported.IsSuccess);
        Assert.Equal(2, imported.Value.Imported);

        var all = (await Rules.GetAllAsync()).OrderByDescending(r => r.Priority).ToList();
        Assert.Equal(2, all.Count);

        var regexRule = all[0];
        Assert.Equal(RuleMatchType.Regex, regexRule.MatchType);
        Assert.Equal(@"^ЛР\d+.*\.docx$", regexRule.Pattern);
        Assert.Equal("ЛР", regexRule.WorkType);
        Assert.Equal("{Subject}_{Type}{Ext}", regexRule.RenameTemplate);
        Assert.Equal(subject.Id, regexRule.SubjectId); // предмет пересопоставлен по имени
        Assert.Equal(_source.Path, regexRule.WatchedFolder);

        var extRule = all[1];
        Assert.False(extRule.Enabled);
        Assert.Null(extRule.WatchedFolder); // «во всех папках» переживает round-trip как null
    }

    [Fact]
    public async Task Blank_watched_folder_is_normalised_to_null()
    {
        // Пустая строка и null означают одно и то же — «во всех папках»; в БД храним только null.
        var created = await Rules.CreateAsync(new ArchivistRule
        {
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            WatchedFolder = "   ",
        });

        var rule = await RuleRepo.GetByIdAsync(created.Value);

        Assert.Null(rule!.WatchedFolder);
    }

    [Fact]
    public async Task Watched_folder_is_normalised_on_save()
    {
        // Хвостовой разделитель не должен мешать сопоставлению с корнем наблюдения в движке.
        var created = await Rules.CreateAsync(new ArchivistRule
        {
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            WatchedFolder = _source.Path + Path.DirectorySeparatorChar,
        });

        var rule = await RuleRepo.GetByIdAsync(created.Value);

        Assert.Equal(_source.Path, rule!.WatchedFolder);
    }

    [Fact]
    public async Task Import_append_keeps_existing_rules_and_warns_about_missing_subjects()
    {
        await Rules.CreateAsync(new ArchivistRule { Pattern = ".keep", MatchType = RuleMatchType.Extension });

        var json = """
        { "schema": "rubrica.archivist.rules", "version": 1, "rules": [
          { "pattern": ".new", "matchType": "Extension", "priority": 7, "enabled": true, "subjectName": "Физика" }
        ] }
        """;
        var file = _source.WriteFile("import.json", json);

        var result = await Rules.ImportFromFileAsync(file, RulesImportMode.Append);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Imported);
        Assert.Single(result.Value.Warnings);
        Assert.Equal(2, (await Rules.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Import_rejects_a_file_that_is_not_a_rules_bundle()
    {
        var file = _source.WriteFile("nope.json", "{\"hello\":\"world\"}");

        var result = await Rules.ImportFromFileAsync(file, RulesImportMode.Replace);

        Assert.True(result.IsFailure);
        Assert.Equal("archivist.import_bad_format", result.Error.Code);
    }

    [Fact]
    public async Task Rule_with_unknown_subject_is_rejected()
    {
        var result = await Rules.CreateAsync(new ArchivistRule
        {
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            SubjectId = Guid.NewGuid(),
        });

        Assert.True(result.IsFailure);
        Assert.Equal("archivist.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Rule_lifecycle_works_end_to_end()
    {
        var subject = await SeedSubjectAsync();

        var created = await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 5,
            Enabled = true,
        });
        Assert.True(created.IsSuccess);

        Assert.Single(await Rules.GetActiveAsync());

        Assert.True((await Rules.SetEnabledAsync(created.Value, false)).IsSuccess);
        Assert.Empty(await Rules.GetActiveAsync());
        Assert.Single(await Rules.GetAllAsync());

        Assert.True((await Rules.DeleteAsync(created.Value)).IsSuccess);
        Assert.Empty(await Rules.GetAllAsync());
    }

    [Fact]
    public async Task Manual_sort_moves_file_into_subject_folder()
    {
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        var path = _source.WriteFile("конспект.pdf", "содержимое");
        var record = await SeedFileRecordAsync(path);

        var result = await CreateUnsortedService().SortManuallyAsync(record.Id, subject.Id);

        Assert.True(result.IsSuccess);
        var moved = Path.Combine(_archive.Combine("Матан"), "конспект.pdf");
        Assert.True(File.Exists(moved));
        // Имя при ручной сортировке не меняется - результат предсказуем для пользователя.
        Assert.Equal("содержимое", await File.ReadAllTextAsync(moved));
    }

    [Fact]
    public async Task Manual_sort_without_target_folder_reports_error_and_keeps_file()
    {
        var subject = await SeedSubjectAsync("Дискретка");
        var path = _source.WriteFile("конспект.pdf", "содержимое");
        var record = await SeedFileRecordAsync(path);

        var result = await CreateUnsortedService().SortManuallyAsync(record.Id, subject.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("archivist.target_not_set", result.Error.Code);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Archive_root_is_used_when_subject_has_no_folder()
    {
        Options.CurrentValue = new ArchivistOptions { ArchiveRootFolder = _archive.Path };
        var subject = await SeedSubjectAsync("Дискретка");
        var path = _source.WriteFile("конспект.pdf", "содержимое");
        var record = await SeedFileRecordAsync(path);

        var result = await CreateUnsortedService().SortManuallyAsync(record.Id, subject.Id);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(_archive.Combine("Дискретка"), "конспект.pdf")));
    }

    [Fact]
    public async Task Forgetting_a_record_leaves_the_file_untouched()
    {
        var path = _source.WriteFile("личное.txt", "содержимое");
        var record = await SeedFileRecordAsync(path);
        var service = CreateUnsortedService();

        Assert.True((await service.ForgetAsync(record.Id)).IsSuccess);

        Assert.True(File.Exists(path));
        Assert.Equal("содержимое", await File.ReadAllTextAsync(path));
        Assert.Empty(await service.GetUnsortedAsync());
    }

    [Fact]
    public async Task Unsorted_list_hides_records_whose_files_are_gone()
    {
        var path = _source.WriteFile("исчезнет.txt", "содержимое");
        await SeedFileRecordAsync(path);
        var service = CreateUnsortedService();

        Assert.Single(await service.GetUnsortedAsync());

        File.Delete(path);

        Assert.Empty(await service.GetUnsortedAsync());
    }

    public void Dispose()
    {
        _source.Dispose();
        _archive.Dispose();
    }
}
