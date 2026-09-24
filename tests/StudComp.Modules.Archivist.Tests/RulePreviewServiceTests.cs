using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Live-preview правила (ARCHITECTURE §8.7): сухой прогон по наблюдаемой папке. Главная гарантия —
/// ни один файл не двигается.
/// </summary>
public sealed class RulePreviewServiceTests : ArchivistDatabaseTestBase, IDisposable
{
    private readonly TempFolder _watched = new("preview-src");
    private readonly IFileWatcherService _watcher = Substitute.For<IFileWatcherService>();

    private RulePreviewService CreateService()
    {
        _watcher.WatchedFolders.Returns([_watched.Path]);
        return new RulePreviewService(
            _watcher, Rules, Engine, FileSystem, Options, NullLogger<RulePreviewService>.Instance);
    }

    [Fact]
    public async Task Preview_returns_only_files_matching_the_draft_and_moves_nothing()
    {
        var subject = await SeedSubjectAsync("Матан", @"C:\Архив\Матан");
        _watched.WriteFile("лекция1.pdf", "a");
        _watched.WriteFile("лекция2.pdf", "b");
        _watched.WriteFile("семинар.pdf", "c");
        _watched.WriteFile("таблица.xlsx", "d");

        var draft = new ArchivistRule
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            Pattern = "лекция",
            MatchType = RuleMatchType.Keyword,
        };

        var items = await CreateService().PreviewAsync(draft);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Contains("лекция", i.FileName));
        // Папка не изменилась.
        Assert.Equal(4, Directory.GetFiles(_watched.Path).Length);
    }

    [Fact]
    public async Task Preview_flags_files_that_a_higher_priority_rule_grabs_first()
    {
        var matan = await SeedSubjectAsync("Матан", @"C:\Архив\Матан");
        var lectures = await SeedSubjectAsync("Лекции", @"C:\Архив\Лекции");
        _watched.WriteFile("лекция_матан.docx", "x");

        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = lectures.Id,
            Pattern = "лекция",
            MatchType = RuleMatchType.Keyword,
            Priority = 100,
            Enabled = true,
        });

        var draft = new ArchivistRule
        {
            Id = Guid.NewGuid(),
            SubjectId = matan.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 10,
        };

        var item = Assert.Single(await CreateService().PreviewAsync(draft));

        Assert.True(item.AlreadyHandledByOtherRule);
    }

    [Fact]
    public async Task Preview_respects_the_ignore_list()
    {
        var subject = await SeedSubjectAsync("Матан", @"C:\Архив\Матан");
        _watched.WriteFile("отчёт.tmp", "junk");
        _watched.WriteFile("отчёт.docx", "real");

        var draft = new ArchivistRule
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            Pattern = "отчёт",
            MatchType = RuleMatchType.Keyword,
        };

        var item = Assert.Single(await CreateService().PreviewAsync(draft));
        Assert.Equal("отчёт.docx", item.FileName);
    }

    [Fact]
    public async Task Preview_is_empty_when_no_folder_is_watched()
    {
        _watcher.WatchedFolders.Returns([]);
        var service = new RulePreviewService(
            _watcher, Rules, Engine, FileSystem, Options, NullLogger<RulePreviewService>.Instance);

        var items = await service.PreviewAsync(new ArchivistRule
        {
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
        });

        Assert.Empty(items);
    }

    [Fact]
    public async Task Preview_covers_every_watched_folder()
    {
        using var second = new TempFolder("preview-src2");
        var subject = await SeedSubjectAsync("Матан", @"C:\Архив\Матан");
        _watched.WriteFile("лекция1.pdf", "a");
        second.WriteFile("лекция2.pdf", "b");

        _watcher.WatchedFolders.Returns([_watched.Path, second.Path]);
        var service = new RulePreviewService(
            _watcher, Rules, Engine, FileSystem, Options, NullLogger<RulePreviewService>.Instance);

        var items = await service.PreviewAsync(new ArchivistRule
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            Pattern = "лекция",
            MatchType = RuleMatchType.Keyword,
        });

        Assert.Equal(
            new[] { "лекция1.pdf", "лекция2.pdf" },
            items.Select(i => i.FileName).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task Preview_of_a_folder_scoped_rule_looks_only_in_that_folder()
    {
        using var second = new TempFolder("preview-src2");
        var subject = await SeedSubjectAsync("Матан", @"C:\Архив\Матан");
        _watched.WriteFile("лекция1.pdf", "a");
        second.WriteFile("лекция2.pdf", "b");

        _watcher.WatchedFolders.Returns([_watched.Path, second.Path]);
        var service = new RulePreviewService(
            _watcher, Rules, Engine, FileSystem, Options, NullLogger<RulePreviewService>.Instance);

        var items = await service.PreviewAsync(new ArchivistRule
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            Pattern = "лекция",
            MatchType = RuleMatchType.Keyword,
            WatchedFolder = second.Path,
        });

        Assert.Equal("лекция2.pdf", Assert.Single(items).FileName);
    }

    public void Dispose() => _watched.Dispose();
}
