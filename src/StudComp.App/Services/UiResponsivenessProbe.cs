using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;

namespace StudComp.Services;

/// <summary>
/// Текущая «активность» интерфейса — короткая подпись для строк пробы отзывчивости
/// («Навигация → Органайзер», «Настройки → Архивариус»). Ставится в точках, где пользователь
/// ждёт отклика; стоимость — присваивание строки, при выключенной пробе больше ничего не происходит.
/// </summary>
public static class UiActivity
{
    private static string _current = "простой";

    /// <summary>Что интерфейс делает сейчас (или делал последним).</summary>
    public static string Current => Volatile.Read(ref _current);

    /// <summary>Отметить начало активности, которую пользователь ждёт.</summary>
    public static void Mark(string activity) => Volatile.Write(ref _current, activity);
}

/// <summary>
/// Проба отзывчивости UI-потока (Phase 13.10): фоновый цикл раз в 50 мс ставит в очередь
/// диспетчера пустую операцию с приоритетом <see cref="DispatcherPriority.Input"/> и меряет, через
/// сколько она выполнилась. Пока UI-поток занят (синхронная работа, тяжёлый layout, рендер),
/// операция ждёт — и эта задержка и есть то, что пользователь ощущает как «подвисание».
/// Всё дольше <see cref="ThresholdMs"/> пишется в лог вместе с текущей активностью.
/// </summary>
/// <remarks>
/// По умолчанию бездействует — включается тем же флагом
/// <see cref="DiagnosticsOptions.PerformanceLoggingEnabled"/>, что и <see cref="PerformanceProbe"/>.
/// Приоритет <c>Input</c> выбран намеренно: он ниже <c>Render</c>/<c>Loaded</c>, поэтому замер
/// включает и уже поставленный в очередь проход layout — ровно момент, когда интерфейс снова
/// готов принять клик.
/// </remarks>
internal sealed class UiResponsivenessProbe(
    IOptions<DiagnosticsOptions> options,
    ILogger<UiResponsivenessProbe> logger) : BackgroundService
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Порог, с которого задержка считается заметной и попадает в лог.</summary>
    private const int ThresholdMs = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PerformanceLoggingEnabled)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        logger.LogInformation("Проба отзывчивости UI включена: порог {ThresholdMs} мс", ThresholdMs);

        using var timer = new PeriodicTimer(ProbeInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var activity = UiActivity.Current;
                var clock = Stopwatch.StartNew();
                await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Input, stoppingToken)
                    .Task.ConfigureAwait(false);
                clock.Stop();

                if (clock.ElapsedMilliseconds >= ThresholdMs)
                {
                    logger.LogInformation(
                        "UI-поток занят {Ms} мс во время «{Activity}»",
                        clock.ElapsedMilliseconds,
                        activity);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение при остановке хоста (или диспетчер выключился раньше него).
        }
    }
}
