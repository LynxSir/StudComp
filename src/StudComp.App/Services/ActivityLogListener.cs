using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Settings;

namespace StudComp.Services;

/// <summary>
/// Пишет в ленту активности события, которые не проходят через App-VM: разложенные архивариусом файлы
/// (по <see cref="FileSortedMessage"/> из шины). Здесь же — периодический ретеншн-прунинг ленты
/// (параметры — <c>Rubrica:Data</c>, new_addons.md §7 §9).
/// </summary>
internal sealed class ActivityLogListener(
    IMessenger messenger,
    IActivityRepository activityRepository,
    IOptionsMonitor<DataOptions> dataOptions,
    ILogger<ActivityLogListener> logger) : IHostedService, IRecipient<FileSortedMessage>, IDisposable
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);

    private CancellationTokenSource? _cts;
    private Task? _pruneLoop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        messenger.Register(this);
        _cts = new CancellationTokenSource();
        _pruneLoop = RunPruneLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        messenger.Unregister<FileSortedMessage>(this);
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        if (_pruneLoop is not null)
        {
            try
            {
                await _pruneLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // штатно
            }
        }
    }

    public void Receive(FileSortedMessage message)
    {
        _ = WriteSortedAsync(message);
    }

    private async Task WriteSortedAsync(FileSortedMessage message)
    {
        try
        {
            await activityRepository.AddAsync(new ActivityEntry
            {
                Id = Guid.NewGuid(),
                Kind = ActivityKind.FileSorted,
                Timestamp = message.SortedAt,
                SubjectId = message.SubjectId,
                Path = message.FinalPath,
                RefId = message.FileRecordId,
                Title = message.FileName,
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось записать в ленту активности разложенный файл {File}", message.FileName);
        }
    }

    private async Task RunPruneLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            await PruneOnceAsync(ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(PruneInterval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await PruneOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // штатное завершение
        }
    }

    private async Task PruneOnceAsync(CancellationToken ct)
    {
        try
        {
            var options = dataOptions.CurrentValue;
            var keepCount = Math.Max(0, options.ActivityRetentionKeepCount);
            var maxAge = TimeSpan.FromDays(Math.Max(1, options.ActivityRetentionDays));
            var deleted = await activityRepository.PruneAsync(keepCount, maxAge, ct).ConfigureAwait(false);
            if (deleted > 0)
            {
                logger.LogInformation("Лента активности: удалено {Count} старых записей", deleted);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось выполнить ретеншн ленты активности");
        }
    }

    public void Dispose() => _cts?.Dispose();
}
