using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Ретеншн «Корзины» карточек (new_addons.md §3.5, §11): раз в сутки вычищает то, что удалено
/// раньше <see cref="DataOptions.CardTrashRetentionDays"/> назад.
/// </summary>
/// <remarks>
/// Не переиспользует каркас <c>CardsReminderHostedService</c>: там точное время дня и тихие часы,
/// здесь — простое «раз в сутки», без привязки к часу. Свой, более простой цикл честнее общего.
/// </remarks>
internal sealed class CardTrashRetentionHostedService(
    ICardService cards,
    IOptionsMonitor<DataOptions> dataOptions,
    ILogger<CardTrashRetentionHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Ретеншн не имеет права уронить хост: подождём и попробуем в следующий раз.
                logger.LogWarning(ex, "Не удалось очистить устаревшие записи корзины картотеки.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Один проход — тестовый шов, как <c>EvaluateOnceAsync</c> у соседних напоминаний.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var retentionDays = dataOptions.CurrentValue.CardTrashRetentionDays;
        if (retentionDays <= 0)
        {
            return 0;
        }

        var purged = await cards.PurgeExpiredTrashAsync(TimeSpan.FromDays(retentionDays), ct).ConfigureAwait(false);
        if (purged > 0)
        {
            logger.LogInformation("Ретеншн корзины картотеки: удалено безвозвратно {Count} карточек.", purged);
        }

        return purged;
    }
}
