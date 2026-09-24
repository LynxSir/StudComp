using Microsoft.EntityFrameworkCore;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Страж Phase 13.10: продолжение после <c>CreateContextAsync</c> обязано выполняться на пуле потоков,
/// а не на вызывающем. SQLite завершает <c>*Async</c> синхронно, поэтому без этого переключения
/// каждый запрос из ViewModel бежал бы на UI-потоке.
/// </summary>
public sealed class RepositoryBaseTests : DatabaseTestBase
{
    [Fact]
    public async Task Continuation_after_CreateContextAsync_runs_on_a_thread_pool_thread()
    {
        var probe = new ProbeRepository(Factory);

        // Вызываем с выделенного (не пулового) потока — так «остался на том же потоке» и «ушёл на
        // пул» различимы однозначно; в самом xUnit тест уже может идти на пуле.
        var completion = new TaskCompletionSource<(int CallerThread, int WorkThread, bool WorkOnPool, int Rows)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                var callerThread = Environment.CurrentManagedThreadId;
                var task = probe.CountSubjectsAsync();
                var (workThread, onPool, rows) = task.GetAwaiter().GetResult();
                completion.SetResult((callerThread, workThread, onPool, rows));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "caller",
        };
        thread.Start();

        var result = await completion.Task;

        Assert.NotEqual(result.CallerThread, result.WorkThread);
        Assert.True(result.WorkOnPool, "запрос должен выполняться на потоке пула");
        Assert.Equal(0, result.Rows);
    }

    [Fact]
    public async Task Cancelled_token_throws_before_context_is_created()
    {
        var probe = new ProbeRepository(Factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.CountSubjectsAsync(cts.Token));
    }

    private sealed class ProbeRepository(IDbContextFactory<StudCompDbContext> factory) : RepositoryBase(factory)
    {
        public async Task<(int Thread, bool OnPool, int Rows)> CountSubjectsAsync(CancellationToken ct = default)
        {
            await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
            var rows = await context.Subjects.CountAsync(ct).ConfigureAwait(false);
            return (Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread, rows);
        }
    }
}
