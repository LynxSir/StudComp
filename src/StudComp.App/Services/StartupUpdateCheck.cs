using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Infrastructure.Startup;

namespace StudComp.Services;

/// <summary>
/// Разовая фоновая проверка обновлений при запуске (ARCHITECTURE §11.6): единственный сетевой вызов,
/// с opt-out. По образцу <see cref="PerformanceProbe"/> — само-гейт по настройке, ничего не делает,
/// если проверка выключена или недоступна. Задержка перед запросом — чтобы не утяжелять холодный
/// старт (NFR §14).
/// </summary>
internal sealed class StartupUpdateCheck(
    IUpdateService updates,
    IToastService toasts,
    IOptions<UpdateOptions> options,
    ILogger<StartupUpdateCheck> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AutoCheckOnStartup || !updates.IsUpdateSupported)
        {
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            var update = await updates.CheckAsync(stoppingToken).ConfigureAwait(false);
            if (update is null)
            {
                return;
            }

            toasts.ShowAction(
                "Доступно обновление Rubrica",
                $"Версия {update.Version} готова к установке.",
                "Обновить и перезапустить",
                async () =>
                {
                    await updates.DownloadAsync(update).ConfigureAwait(false);
                    updates.ApplyAndRestart(update);
                });
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение при остановке хоста.
        }
        catch (Exception ex)
        {
            // Проверка обновлений не имеет права мешать работе приложения.
            logger.LogWarning(ex, "Фоновая проверка обновлений не удалась");
        }
    }
}
