using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>Расписание и отсев отложенной очереди ретрая (ARCHITECTURE §8.6). Чистая логика, без диска.</summary>
public sealed class DeferredRetryQueueTests
{
    private static readonly ArchivistOptions Options = new()
    {
        DeferredRetryInitialSeconds = 30,
        DeferredRetryMaxAttempts = 3,
        DeferredRetryMaxIntervalSeconds = 1800,
    };

    [Fact]
    public void Tracked_path_is_not_due_before_its_backoff_elapses()
    {
        var queue = new DeferredRetryQueue();
        var now = DateTimeOffset.UtcNow;

        queue.Track("a.docx", Options, now);

        Assert.Equal(1, queue.Count);
        var (due, exhausted) = queue.TakeDue(Options, now.AddSeconds(10));
        Assert.Empty(due);
        Assert.Empty(exhausted);
    }

    [Fact]
    public void Path_becomes_due_after_the_first_backoff_window()
    {
        var queue = new DeferredRetryQueue();
        var now = DateTimeOffset.UtcNow;
        queue.Track("a.docx", Options, now);

        var (due, _) = queue.TakeDue(Options, now.AddSeconds(31));

        Assert.Equal(["a.docx"], due);
    }

    [Fact]
    public void Backoff_grows_between_attempts()
    {
        var queue = new DeferredRetryQueue();
        var now = DateTimeOffset.UtcNow;
        queue.Track("a.docx", Options, now);

        // Первое окно ~30 с.
        Assert.Single(queue.TakeDue(Options, now.AddSeconds(31)).Due);
        // Второе окно уже ~120 с — на 60-й секунде ещё рано.
        Assert.Empty(queue.TakeDue(Options, now.AddSeconds(60)).Due);
        Assert.Single(queue.TakeDue(Options, now.AddSeconds(200)).Due);
    }

    [Fact]
    public void After_max_attempts_the_path_is_reported_exhausted_and_removed()
    {
        var queue = new DeferredRetryQueue();
        var now = DateTimeOffset.UtcNow;
        queue.Track("a.docx", Options, now);

        // MaxAttempts = 3: две успешные выдачи на ретрай, третья попытка исчерпывает лимит.
        queue.TakeDue(Options, now.AddHours(1));
        queue.TakeDue(Options, now.AddHours(2));
        var (due, exhausted) = queue.TakeDue(Options, now.AddHours(3));

        Assert.Empty(due);
        Assert.Equal(["a.docx"], exhausted);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Forget_drops_a_path_from_the_queue()
    {
        var queue = new DeferredRetryQueue();
        queue.Track("a.docx", Options, DateTimeOffset.UtcNow);

        queue.Forget("a.docx");

        Assert.Equal(0, queue.Count);
    }
}
