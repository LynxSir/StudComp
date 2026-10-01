using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Уборка осиротевших картинок заметок: сессии корзины старше суток уходят в «Корзину» Windows.
/// Осиротеть они могут только от падения приложения между удалением картинки и закрытием заметки —
/// в обычной жизни их забирает <see cref="INoteImageTrash.CommitAsync"/>.
/// </summary>
/// <remarks>
/// Каркас — как у <c>CardTrashRetentionHostedService</c>: простое «раз в сутки», без привязки к
/// часу. Порог суток выбран так, чтобы возврат по <c>Ctrl+Z</c> и возврат при открытии заметки
/// успели сработать первыми.
/// </remarks>
internal sealed class NoteImageTrashSweepHostedService(
    INoteImageTrash trash,
    ILogger<NoteImageTrashSweepHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Сколько сессия корзины живёт, прежде чем её содержимое уйдёт в «Корзину» Windows.</summary>
    internal static readonly TimeSpan OrphanAge = TimeSpan.FromDays(1);

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
                await trash.SweepOrphansAsync(OrphanAge, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // Уборка не имеет права уронить хост: подождём и попробуем в следующий раз.
                logger.LogWarning(exception, "Не удалось убрать осиротевшие картинки заметок.");
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
}
