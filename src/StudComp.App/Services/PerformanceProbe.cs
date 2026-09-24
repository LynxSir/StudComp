using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;

namespace StudComp.Services;

/// <summary>
/// Периодически пишет в лог снимок ресурсов процесса — чтобы NFR §14 (память простаивающего
/// приложения ~50–80 МБ) подтверждались измерением, а не на глаз.
/// </summary>
/// <remarks>
/// По умолчанию бездействует: включается флагом <see cref="DiagnosticsOptions.PerformanceLoggingEnabled"/>
/// на время замера. Ничего не чинит и ни на что не влияет — только читает счётчики и логирует.
/// </remarks>
internal sealed class PerformanceProbe(
    IOptions<DiagnosticsOptions> options,
    ILogger<PerformanceProbe> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.PerformanceLoggingEnabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.ProbeIntervalMinutes));
        logger.LogInformation("Проба производительности включена, интервал {Minutes} мин", interval.TotalMinutes);

        // Первый снимок сразу — «стартовое» состояние сразу после отрисовки окна.
        LogSnapshot("старт");

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                LogSnapshot("простой");
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение при остановке хоста.
        }
    }

    private void LogSnapshot(string phase)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var workingSetMb = process.WorkingSet64 / (1024.0 * 1024.0);
        var privateMb = process.PrivateMemorySize64 / (1024.0 * 1024.0);
        var managedMb = GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0);
        ThreadPool.GetAvailableThreads(out var workerThreads, out _);

        logger.LogInformation(
            "Производительность ({Phase}): рабочий набор {WorkingSetMb:F1} МБ, приватная {PrivateMb:F1} МБ, "
            + "управляемая куча {ManagedMb:F1} МБ, хендлов {Handles}, потоков {Threads}, свободных worker-потоков {FreeWorkers}",
            phase,
            workingSetMb,
            privateMb,
            managedMb,
            process.HandleCount,
            process.Threads.Count,
            workerThreads);
    }
}
