using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Живучесть наблюдателей (ARCHITECTURE §8.5): переполнение внутреннего буфера форсирует сверку, а
/// отвалившийся <c>FileSystemWatcher</c> поднимается health-циклом. Реальные таймеры здесь не
/// крутятся — один шаг вызывается напрямую, как <c>ProcessFileAsync</c> в интеграционных тестах.
/// </summary>
public sealed class WatcherResilienceTests : ArchivistDatabaseTestBase, IDisposable
{
    private readonly TempFolder _first = new("health-a");
    private readonly TempFolder _second = new("health-b");
    private readonly ReconciliationSignal _signal = new();

    private FileWatcherHostedService CreateWorker(params string[] folders)
    {
        Options.CurrentValue = new ArchivistOptions
        {
            Enabled = true,
            WatchedFolders = [.. folders],
            MinFileSizeBytes = 0,
            StabilityProbeIntervalMs = 20,
            StabilityTimeoutSeconds = 2,
        };

        return new FileWatcherHostedService(
            FileRecordRepo,
            SubjectRepo,
            Rules,
            Engine,
            Executor,
            new FileStabilityChecker(FileSystem, Options, NullLogger<FileStabilityChecker>.Instance),
            Hasher,
            FileSystem,
            Substitute.For<IToastService>(),
            new WeakReferenceMessenger(),
            _signal,
            Workspace,
            Options,
            NullLogger<FileWatcherHostedService>.Instance);
    }

    [Fact]
    public async Task Buffer_overflow_forces_a_reconciliation_run()
    {
        // §8.5: при переполнении буфера события точно потеряны — единственный способ их вернуть
        // это немедленная сверка. Сам watcher при этом живой, пересоздавать его незачем.
        var worker = CreateWorker(_first.Path);
        worker.RaiseWatcherErrorForTests(_first.Path, new InternalBufferOverflowException("буфер"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var reason = await _signal.WaitAsync(cts.Token);

        Assert.Contains(_first.Path, reason);
        GC.KeepAlive(worker);
    }

    [Fact]
    public async Task Ordinary_watcher_error_recreates_the_watcher_instead_of_forcing_reconciliation()
    {
        var worker = CreateWorker(_first.Path);
        worker.ApplySettingsForTests();
        Assert.True(worker.IsWatching);

        worker.RaiseWatcherErrorForTests(_first.Path, new IOException("наблюдатель умер"));

        // Наблюдатель поднят заново и сигнала внеочередной сверки не было.
        Assert.Contains(_first.Path, worker.WatchedFolders);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _signal.WaitAsync(cts.Token));

        worker.Dispose();
    }

    [Fact]
    public void Health_check_revives_a_watcher_that_stopped_raising_events()
    {
        var worker = CreateWorker(_first.Path, _second.Path);
        worker.ApplySettingsForTests();
        Assert.Equal(2, worker.WatchedFolders.Count);

        // Именно так ведёт себя FileSystemWatcher, переживший пересоздание своей папки: объект жив,
        // но события больше не шлёт (ARCHITECTURE §8.5).
        worker.DisableWatcherForTests(_first.Path);

        worker.CheckWatcherHealth();

        Assert.Equal(2, worker.WatchedFolders.Count);
        Assert.True(worker.IsWatcherHealthyForTests(_first.Path));
        Assert.True(worker.IsWatcherHealthyForTests(_second.Path));

        worker.Dispose();
    }

    [Fact]
    public void Health_check_leaves_a_missing_folder_alone()
    {
        var worker = CreateWorker(_first.Path, @"C:\такой-папки-нет-12345");
        worker.ApplySettingsForTests();

        // Несуществующая папка не поднимается и не мешает остальным.
        Assert.Equal([_first.Path], worker.WatchedFolders);

        worker.CheckWatcherHealth();

        Assert.Equal([_first.Path], worker.WatchedFolders);
        worker.Dispose();
    }

    [Fact]
    public void All_configured_folders_get_their_own_watcher()
    {
        var worker = CreateWorker(_first.Path, _second.Path);
        worker.ApplySettingsForTests();

        Assert.Equal(
            new[] { _first.Path, _second.Path }.OrderBy(p => p),
            worker.WatchedFolders.OrderBy(p => p));

        worker.Dispose();
    }

    [Fact]
    public void Duplicate_folders_in_settings_produce_one_watcher()
    {
        var worker = CreateWorker(_first.Path, _first.Path + Path.DirectorySeparatorChar);
        worker.ApplySettingsForTests();

        Assert.Single(worker.WatchedFolders);
        worker.Dispose();
    }

    [Fact]
    public void Reapplying_unchanged_settings_neither_rebuilds_watchers_nor_raises_StateChanged()
    {
        // Phase 13.10: монитор настроек дёргает ApplySettings на любую запись usersettings.json
        // (и обычно дважды). Без изменений в самих настройках наблюдения это должно быть no-op —
        // иначе каждый тумблер оформления пересканировал бы все папки и будил UI штормом событий.
        var worker = CreateWorker(_first.Path, _second.Path);
        worker.ApplySettingsForTests();

        var raised = 0;
        worker.StateChanged += (_, _) => raised++;

        worker.ApplySettingsForTests();
        worker.ApplySettingsForTests();

        Assert.Equal(0, raised);
        Assert.Equal(2, worker.WatchedFolders.Count);

        // А настоящая смена настроек по-прежнему применяется и сообщает о себе.
        Options.CurrentValue.WatchedFolders = [_first.Path];
        worker.ApplySettingsForTests();

        Assert.Equal(1, raised);
        Assert.Equal([_first.Path], worker.WatchedFolders);
        worker.Dispose();
    }

    [Fact]
    public void Disabling_the_archivist_tears_down_every_watcher()
    {
        var worker = CreateWorker(_first.Path, _second.Path);
        worker.ApplySettingsForTests();
        Assert.Equal(2, worker.WatchedFolders.Count);

        Options.CurrentValue.Enabled = false;
        worker.ApplySettingsForTests();

        Assert.Empty(worker.WatchedFolders);
        Assert.False(worker.IsWatching);
        worker.Dispose();
    }

    public void Dispose()
    {
        _first.Dispose();
        _second.Dispose();
    }
}
