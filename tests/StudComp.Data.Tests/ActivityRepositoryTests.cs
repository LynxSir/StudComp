using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Тесты <see cref="ActivityRepository"/> на реальном temp-SQLite (Phase 12.1). Лента активности —
/// источник данных для Дашборда, с ретеншном по возрасту и количеству.
/// </summary>
public sealed class ActivityRepositoryTests : DatabaseTestBase
{
    private static ActivityEntry Entry(ActivityKind kind, DateTimeOffset timestamp, Guid? subjectId = null) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Timestamp = timestamp,
        SubjectId = subjectId,
        Path = @"C:\Учёба\Матан\лекция.pdf",
        Title = "лекция.pdf",
    };

    [Fact]
    public async Task GetRecentAsync_returns_newest_first_and_respects_take()
    {
        var repo = new ActivityRepository(Factory);
        var now = DateTimeOffset.UtcNow;

        await repo.AddAsync(Entry(ActivityKind.FileOpened, now.AddMinutes(-30)));
        await repo.AddAsync(Entry(ActivityKind.FileSorted, now.AddMinutes(-10)));
        await repo.AddAsync(Entry(ActivityKind.ReportGenerated, now.AddMinutes(-1)));

        var recent = await repo.GetRecentAsync(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal(ActivityKind.ReportGenerated, recent[0].Kind);
        Assert.Equal(ActivityKind.FileSorted, recent[1].Kind);
    }

    [Fact]
    public async Task GetRecentByKindsAsync_filters_to_the_requested_kinds()
    {
        var repo = new ActivityRepository(Factory);
        var now = DateTimeOffset.UtcNow;

        await repo.AddAsync(Entry(ActivityKind.FileOpened, now.AddMinutes(-5)));
        await repo.AddAsync(Entry(ActivityKind.ReportGenerated, now.AddMinutes(-4)));
        await repo.AddAsync(Entry(ActivityKind.FileSorted, now.AddMinutes(-3)));

        var files = await repo.GetRecentByKindsAsync(10, [ActivityKind.FileOpened, ActivityKind.FileSorted]);

        Assert.Equal(2, files.Count);
        Assert.All(files, e => Assert.Contains(e.Kind, new[] { ActivityKind.FileOpened, ActivityKind.FileSorted }));
    }

    [Fact]
    public async Task PruneAsync_drops_rows_older_than_maxAge()
    {
        var repo = new ActivityRepository(Factory);
        var now = DateTimeOffset.UtcNow;

        await repo.AddAsync(Entry(ActivityKind.FileOpened, now.AddDays(-120)));
        await repo.AddAsync(Entry(ActivityKind.FileOpened, now.AddDays(-100)));
        await repo.AddAsync(Entry(ActivityKind.FileOpened, now.AddDays(-1)));

        var deleted = await repo.PruneAsync(keep: 500, maxAge: TimeSpan.FromDays(90));

        Assert.Equal(2, deleted);
        var left = await repo.GetRecentAsync(10);
        Assert.Single(left);
    }

    [Fact]
    public async Task PruneAsync_keeps_only_the_newest_N()
    {
        var repo = new ActivityRepository(Factory);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 10; i++)
        {
            await repo.AddAsync(Entry(ActivityKind.FileOpened, now.AddMinutes(-i)));
        }

        var deleted = await repo.PruneAsync(keep: 4, maxAge: TimeSpan.FromDays(365));

        Assert.Equal(6, deleted);
        Assert.Equal(4, (await repo.GetRecentAsync(100)).Count);
    }

    [Fact]
    public async Task Deleting_a_subject_nulls_the_activity_reference()
    {
        var subject = TestData.Subject();
        await using (var context = CreateContext())
        {
            context.Subjects.Add(subject);
            await context.SaveChangesAsync();
        }

        var repo = new ActivityRepository(Factory);
        await repo.AddAsync(Entry(ActivityKind.FileSorted, DateTimeOffset.UtcNow, subject.Id));

        await using (var context = CreateContext())
        {
            context.Subjects.Remove(await context.Subjects.FindAsync(subject.Id) ?? subject);
            await context.SaveChangesAsync();
        }

        var entry = Assert.Single(await repo.GetRecentAsync(10));
        Assert.Null(entry.SubjectId);
    }
}
