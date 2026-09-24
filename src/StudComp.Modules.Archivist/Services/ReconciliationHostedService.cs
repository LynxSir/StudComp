using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>Итог одного прохода сверки — уходит в лог и в тост по кнопке «Проверить папки сейчас».</summary>
/// <param name="Scanned">Сколько файлов просмотрено на диске.</param>
/// <param name="Missed">Сколько файлов не значились в учёте и отправлены в обычный конвейер.</param>
/// <param name="Recovered">Сколько зависших записей журнала разобрано.</param>
/// <param name="Quarantined">Сколько записей помечено проблемными (файл не нашёлся ни на одном пути).</param>
public readonly record struct ReconciliationReport(int Scanned, int Missed, int Recovered, int Quarantined)
{
    /// <summary>Нашлось ли вообще что-то, о чём стоит сказать пользователю.</summary>
    public bool HasFindings => Missed > 0 || Recovered > 0 || Quarantined > 0;
}

/// <summary>
/// Периодическая сверка «диск ↔ БД» (ARCHITECTURE §8.2, §8.5). Страховка на случай, когда
/// <c>FileSystemWatcher</c> потерял события: при массовом копировании его внутренний буфер
/// переполняется, и часть файлов иначе осталась бы незамеченной до перезапуска приложения.
/// </summary>
public interface IReconciliationService
{
    /// <summary>Попросить внеочередной проход; отработает в фоне, вызывающего не блокирует.</summary>
    void RequestRun(string reason);
}

internal sealed class ReconciliationHostedService(
    IFileWatcherService watcher,
    IFileRecordRepository fileRecords,
    IFileOperationLogRepository operationLog,
    IFileSystem fileSystem,
    ReconciliationSignal signal,
    IOptionsMonitor<ArchivistOptions> options,
    ILogger<ReconciliationHostedService> logger) : BackgroundService, IReconciliationService
{
    // Дать наблюдателю подняться и разобрать стартовый скан, прежде чем сверять его работу.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    // Записи учёта, которые не считаются «файл уже разобран»: их надо брать в работу снова.
    private static readonly FileRecordStatus[] RetryableStatuses =
        [FileRecordStatus.Detected, FileRecordStatus.Deferred];

    public void RequestRun(string reason) => signal.Request(reason);

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

        var period = TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.ReconciliationIntervalMinutes));

        try
        {
            using var timer = new PeriodicTimer(period);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Просыпаемся либо по расписанию, либо по форс-сигналу от наблюдателя
                // (переполнение буфера — §8.5) или по кнопке в UI.
                var tick = timer.WaitForNextTickAsync(stoppingToken).AsTask();
                var forced = signal.WaitAsync(stoppingToken).AsTask();

                var completed = await Task.WhenAny(tick, forced).ConfigureAwait(false);
                if (completed == forced)
                {
                    logger.LogInformation("Внеочередная сверка папок: {Reason}", await forced.ConfigureAwait(false));
                }
                else if (!await tick.ConfigureAwait(false))
                {
                    break;
                }

                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Сбой одного прохода не должен останавливать сверку навсегда (ARCHITECTURE §11.3).
                    logger.LogError(ex, "Проход сверки папок завершился ошибкой");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста.
        }
    }

    /// <summary>
    /// Один полный проход. <c>internal</c> — тесты гоняют его напрямую, без таймеров (тот же приём,
    /// что с <c>ProcessFileAsync</c>).
    /// </summary>
    /// <remarks>
    /// Проход ничего не перемещает и не удаляет: он только находит файлы, о которых учёт не знает, и
    /// приводит журнал в соответствие тому, что уже лежит на диске (ARCHITECTURE §14).
    /// </remarks>
    internal async Task<ReconciliationReport> RunOnceAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return default;
        }

        var (scanned, missed) = await ScanFoldersAsync(current, ct).ConfigureAwait(false);
        var (recovered, quarantined) = await RecoverStalePlannedAsync(current, ct).ConfigureAwait(false);

        var report = new ReconciliationReport(scanned, missed, recovered, quarantined);

        if (report.HasFindings)
        {
            logger.LogInformation(
                "Сверка папок: просмотрено {Scanned}, не в учёте {Missed}, восстановлено записей {Recovered}, "
                + "помечено проблемными {Quarantined}",
                scanned,
                missed,
                recovered,
                quarantined);
        }
        else
        {
            logger.LogDebug("Сверка папок: расхождений нет, просмотрено {Scanned}", scanned);
        }

        return report;
    }

    /// <summary>
    /// Сверка содержимого наблюдаемых папок с <c>FileRecord</c>. Всё, чего в учёте нет (или что учтено
    /// как «ещё не разобрано»), отправляется в очередь наблюдателя — дальше файл идёт обычным
    /// конвейером, дублировать движок правил и исполнителя здесь нечего (ADR §16.53).
    /// </summary>
    private async Task<(int Scanned, int Missed)> ScanFoldersAsync(
        ArchivistOptions current, CancellationToken ct)
    {
        var scanned = 0;
        var missed = new List<PendingFile>();

        foreach (var folder in watcher.WatchedFolders)
        {
            ct.ThrowIfCancellationRequested();

            if (!fileSystem.DirectoryExists(folder))
            {
                continue;
            }

            // Один запрос на папку вместо запроса на каждый файл: DoD говорит о пачках в сотни файлов.
            var known = await fileRecords.GetByOriginalPathPrefixAsync(folder, ct).ConfigureAwait(false);
            var handledPaths = known
                .Where(r => !RetryableStatuses.Contains(r.Status))
                .Select(r => r.OriginalPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> files;
            try
            {
                files = [.. fileSystem.EnumerateFiles(folder)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Сверка: не удалось прочитать содержимое папки {Folder}", folder);
                continue;
            }

            foreach (var path in files)
            {
                scanned++;

                if (handledPaths.Contains(path) || IsNoise(path, current))
                {
                    continue;
                }

                missed.Add(new PendingFile(folder, path));
            }
        }

        if (missed.Count > 0)
        {
            logger.LogInformation(
                "Сверка нашла {Count} файлов, о которых наблюдатель не знал — ставим в очередь", missed.Count);
            watcher.EnqueueMissed(missed);
        }

        return (scanned, missed.Count);
    }

    /// <summary>
    /// Разбор записей журнала, зависших в <see cref="FileOperationLogStatus.Planned"/> — это след
    /// падения между записью журнала и <c>File.Move</c> (ARCHITECTURE §8.4 п.6, §8.6). Смотрим, где
    /// файл оказался на самом деле, и приводим учёт в соответствие; сам файл не трогаем.
    /// </summary>
    private async Task<(int Recovered, int Quarantined)> RecoverStalePlannedAsync(
        ArchivistOptions current, CancellationToken ct)
    {
        var pending = await operationLog.GetPendingAsync(ct).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return (0, 0);
        }

        // Свежие Planned-записи не трогаем: операция может идти прямо сейчас.
        var cutoff = DateTimeOffset.Now - TimeSpan.FromMinutes(Math.Max(1, current.ReconciliationStalePlannedMinutes));

        var recovered = 0;
        var quarantined = 0;

        foreach (var entry in pending.Where(e => e.StartedAt <= cutoff))
        {
            ct.ThrowIfCancellationRequested();

            var record = await fileRecords.GetByIdAsync(entry.FileRecordId, ct).ConfigureAwait(false);

            if (fileSystem.FileExists(entry.PlannedPath))
            {
                // Перемещение прошло, а журнал закрыть не успели.
                entry.Status = FileOperationLogStatus.Completed;
                entry.FinalPath = entry.PlannedPath;
                entry.CompletedAt = DateTimeOffset.Now;
                await operationLog.UpdateAsync(entry, ct).ConfigureAwait(false);

                if (record is not null)
                {
                    record.CurrentPath = entry.PlannedPath;
                    record.Status = FileRecordStatus.Sorted;
                    await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);
                }

                recovered++;
                logger.LogInformation(
                    "Сверка: операция {EntryId} на самом деле завершилась — файл лежит в {Path}",
                    entry.Id,
                    entry.PlannedPath);
            }
            else if (fileSystem.FileExists(entry.OriginalPath))
            {
                // Файл никуда не уехал — вернуть запись в «Обнаружен», конвейер попробует снова.
                entry.Status = FileOperationLogStatus.Failed;
                entry.CompletedAt = DateTimeOffset.Now;
                await operationLog.UpdateAsync(entry, ct).ConfigureAwait(false);

                if (record is not null && record.Status != FileRecordStatus.Sorted)
                {
                    record.CurrentPath = entry.OriginalPath;
                    record.Status = FileRecordStatus.Detected;
                    await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);
                }

                recovered++;
                logger.LogInformation(
                    "Сверка: операция {EntryId} не состоялась — файл остался в {Path}, попробуем снова",
                    entry.Id,
                    entry.OriginalPath);
            }
            else
            {
                // Файла нет ни там, ни там: его унесли мимо нас. Ничего не выдумываем — помечаем
                // проблемным, чтобы пользователь увидел это во «Неразобранном» и в «Журнале».
                entry.Status = FileOperationLogStatus.Failed;
                entry.CompletedAt = DateTimeOffset.Now;
                await operationLog.UpdateAsync(entry, ct).ConfigureAwait(false);

                if (record is not null)
                {
                    record.Status = FileRecordStatus.Quarantined;
                    await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);
                }

                quarantined++;
                logger.LogWarning(
                    "Сверка: файл операции {EntryId} не найден ни в {Original}, ни в {Planned} — "
                    + "запись помечена проблемной",
                    entry.Id,
                    entry.OriginalPath,
                    entry.PlannedPath);
            }
        }

        return (recovered, quarantined);
    }

    /// <summary>Тот же отсев шума, что и в конвейере: игнор-лист, служебные файлы, облачные заглушки.</summary>
    private bool IsNoise(string path, ArchivistOptions current)
    {
        if (IgnoreListMatcher.IsIgnoredName(Path.GetFileName(path), current.IgnoredPatterns))
        {
            return true;
        }

        try
        {
            var attributes = fileSystem.GetAttributes(path);
            return IgnoreListMatcher.IsSystemOrHidden(attributes)
                || (current.SkipCloudPlaceholders && IgnoreListMatcher.IsCloudPlaceholder(attributes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Файл исчез или недоступен прямо сейчас — не наше дело в этом проходе.
            return true;
        }
    }
}
