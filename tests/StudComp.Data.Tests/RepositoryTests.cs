using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// По одному показательному запросу на каждый репозиторий §7.3 (PLAN.md Phase 2 DoD:
/// «репозитории покрывают минимум по одному тестируемому запросу каждый»).
/// </summary>
public sealed class RepositoryTests : DatabaseTestBase
{
    [Fact]
    public async Task SubjectRepository_round_trips_entity()
    {
        var repo = new SubjectRepository(Factory);
        var subject = TestData.Subject("Физика");

        await repo.AddAsync(subject);
        Assert.NotNull(await repo.GetByIdAsync(subject.Id));

        subject.Name = "Физика (переименовано)";
        await repo.UpdateAsync(subject);
        Assert.Equal("Физика (переименовано)", (await repo.GetByIdAsync(subject.Id))!.Name);

        await repo.DeleteAsync(subject.Id);
        Assert.Null(await repo.GetByIdAsync(subject.Id));
    }

    [Fact]
    public async Task DeadlineRepository_GetUpcoming_filters_by_window_and_status()
    {
        var subject = TestData.Subject();
        var overdue = TestData.Deadline(subject.Id, DateTimeOffset.UtcNow.AddDays(-2));
        var soon = TestData.Deadline(subject.Id, DateTimeOffset.UtcNow.AddDays(3));
        var farAway = TestData.Deadline(subject.Id, DateTimeOffset.UtcNow.AddDays(30));
        var doneSoon = TestData.Deadline(subject.Id, DateTimeOffset.UtcNow.AddDays(1), DeadlineStatus.Done);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.Deadlines.AddRange(overdue, soon, farAway, doneSoon);
            await arrange.SaveChangesAsync();
        }

        var repo = new DeadlineRepository(Factory);
        var upcoming = await repo.GetUpcomingAsync(TimeSpan.FromDays(7));

        // Просроченный Pending тоже возвращается — «просрочен» выводит вызывающий (ARCHITECTURE §9.3).
        Assert.Equal([overdue.Id, soon.Id], upcoming.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task GradeRepository_GetBySubject_returns_only_that_subject_ordered_by_date()
    {
        var target = TestData.Subject("Целевой");
        var other = TestData.Subject("Другой");
        var newer = TestData.GradeEntry(target.Id, new DateTime(2026, 3, 1));
        var older = TestData.GradeEntry(target.Id, new DateTime(2026, 1, 1));

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.AddRange(target, other);
            arrange.GradeEntries.AddRange(newer, older, TestData.GradeEntry(other.Id, new DateTime(2026, 2, 1)));
            await arrange.SaveChangesAsync();
        }

        var repo = new GradeRepository(Factory);
        var grades = await repo.GetBySubjectAsync(target.Id);

        Assert.Equal([older.Id, newer.Id], grades.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task ArchivistRuleRepository_returns_enabled_ordered_by_priority_desc()
    {
        await using (var arrange = CreateContext())
        {
            arrange.ArchivistRules.AddRange(
                TestData.ArchivistRule(priority: 1),
                TestData.ArchivistRule(priority: 5),
                TestData.ArchivistRule(priority: 3),
                TestData.ArchivistRule(priority: 99, enabled: false));
            await arrange.SaveChangesAsync();
        }

        var repo = new ArchivistRuleRepository(Factory);
        var rules = await repo.GetEnabledOrderedByPriorityAsync();

        Assert.Equal([5, 3, 1], rules.Select(x => x.Priority).ToArray());
    }

    [Fact]
    public async Task FileRecordRepository_finds_by_content_hash()
    {
        await using (var arrange = CreateContext())
        {
            arrange.FileRecords.AddRange(
                TestData.FileRecord("hash-aaa"),
                TestData.FileRecord("hash-bbb"));
            await arrange.SaveChangesAsync();
        }

        var repo = new FileRecordRepository(Factory);
        var found = await repo.GetByContentHashAsync("hash-bbb");

        Assert.Equal("hash-bbb", Assert.Single(found).ContentHash);
    }

    /// <summary>
    /// Выборка по префиксу папки — горячий путь reconciliation (ARCHITECTURE §8.2): один запрос на
    /// папку вместо запроса на каждый из сотен файлов.
    /// </summary>
    [Fact]
    public async Task FileRecordRepository_finds_records_by_folder_prefix()
    {
        var inside = TestData.FileRecord("hash-1");
        inside.OriginalPath = @"C:\Загрузки\отчёт.docx";

        var lookalike = TestData.FileRecord("hash-2");
        // Ключевой случай: без разделителя в конце префикса эта запись попала бы в выборку.
        lookalike.OriginalPath = @"C:\Загрузки (старое)\отчёт.docx";

        var elsewhere = TestData.FileRecord("hash-3");
        elsewhere.OriginalPath = @"C:\Документы\отчёт.docx";

        await using (var arrange = CreateContext())
        {
            arrange.FileRecords.AddRange(inside, lookalike, elsewhere);
            await arrange.SaveChangesAsync();
        }

        var repo = new FileRecordRepository(Factory);

        var found = await repo.GetByOriginalPathPrefixAsync(@"C:\Загрузки");
        Assert.Equal(@"C:\Загрузки\отчёт.docx", Assert.Single(found).OriginalPath);

        // Хвостовой разделитель в аргументе ничего не меняет.
        Assert.Single(await repo.GetByOriginalPathPrefixAsync(@"C:\Загрузки\"));
        Assert.Empty(await repo.GetByOriginalPathPrefixAsync(@"C:\Пусто"));
    }

    [Fact]
    public async Task ReportJobRepository_GetRecent_returns_newest_first_capped()
    {
        var template = TestData.ReportTemplate();
        var oldest = TestData.ReportJob(template.Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var middle = TestData.ReportJob(template.Id, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var newest = TestData.ReportJob(template.Id, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        await using (var arrange = CreateContext())
        {
            arrange.ReportTemplates.Add(template);
            arrange.ReportJobs.AddRange(oldest, middle, newest);
            await arrange.SaveChangesAsync();
        }

        var repo = new ReportJobRepository(Factory);
        var recent = await repo.GetRecentAsync(2);

        Assert.Equal([newest.Id, middle.Id], recent.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task ReportJobRepository_DeleteAsync_removes_only_target_record()
    {
        var template = TestData.ReportTemplate();
        var target = TestData.ReportJob(template.Id, DateTimeOffset.UtcNow);
        var other = TestData.ReportJob(template.Id, DateTimeOffset.UtcNow);

        await using (var arrange = CreateContext())
        {
            arrange.ReportTemplates.Add(template);
            arrange.ReportJobs.AddRange(target, other);
            await arrange.SaveChangesAsync();
        }

        var repo = new ReportJobRepository(Factory);

        await repo.DeleteAsync(target.Id);

        Assert.Null(await repo.GetByIdAsync(target.Id));
        Assert.NotNull(await repo.GetByIdAsync(other.Id));

        // Повторное удаление несуществующей записи не бросает.
        await repo.DeleteAsync(target.Id);
    }
}
