using System.Threading.Channels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Фоновый наблюдатель за папками (ARCHITECTURE §8.2, §8.5, §11.4). Синхронный callback
/// <see cref="FileSystemWatcher"/> только кладёт путь в <see cref="Channel"/>; вся работа идёт в
/// асинхронном consumer-цикле, поэтому поток событий файловой системы никогда не блокируется.
/// </summary>
/// <remarks>
/// Ошибка на одном файле не должна останавливать воркер (ARCHITECTURE §11.3), поэтому обработка
/// каждого файла обёрнута в try/catch. Наблюдателей столько же, сколько настроенных папок; их живость
/// стережёт отдельный health-цикл, а потерянные события подбирает <c>ReconciliationHostedService</c>.
/// </remarks>
internal sealed class FileWatcherHostedService(
    IFileRecordRepository fileRecords,
    ISubjectRepository subjects,
    IArchivistRuleService rules,
    ISortingRuleEngine engine,
    IFileOperationExecutor executor,
    IFileStabilityChecker stabilityChecker,
    IFileHasher hasher,
    IFileSystem fileSystem,
    IToastService toasts,
    IMessenger messenger,
    ReconciliationSignal reconciliationSignal,
    IStudyWorkspace workspace,
    IOptionsMonitor<ArchivistOptions> options,
    ILogger<FileWatcherHostedService> logger) : BackgroundService, IFileWatcherService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DuplicateEventWindow = TimeSpan.FromSeconds(2);

    // Буфер между callback-ом ФС и обработкой. При переполнении новые события отбрасываются с
    // предупреждением: файл остаётся лежать в папке и будет подобран сверкой (ARCHITECTURE §8.5).
    private readonly Channel<PendingFile> _queue = Channel.CreateBounded<PendingFile>(
        new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private readonly Dictionary<string, DateTimeOffset> _recentEvents = [];
    private readonly Lock _watcherGate = new();

    // Ключ — нормализованный путь папки, значение — её наблюдатель.
    private readonly Dictionary<string, FileSystemWatcher> _watchers =
        new(StringComparer.OrdinalIgnoreCase);

    // Папки из настроек, какими их видел последний ApplySettings: по ним ходит health-check и сверка.
    private volatile string[] _configuredFolders = [];

    // Снимок настроек, применённый последним ApplySettings (см. его remarks).
    private string? _appliedSnapshot;

    // Залоченные файлы: наблюдатель возвращается к ним по бэкоффу, не дожидаясь нового события ФС.
    private readonly DeferredRetryQueue _retryQueue = new();

    private IDisposable? _optionsSubscription;

    public bool IsWatching
    {
        get
        {
            lock (_watcherGate)
            {
                return _watchers.Count > 0;
            }
        }
    }

    public IReadOnlyList<string> WatchedFolders
    {
        get
        {
            lock (_watcherGate)
            {
                return [.. _watchers.Keys];
            }
        }
    }

    public IReadOnlyList<string> ConfiguredFolders => _configuredFolders;

    public event EventHandler? StateChanged;

    public void RequestRescan()
    {
        foreach (var folder in _configuredFolders)
        {
            EnqueueFolder(folder);
        }
    }

    public void EnqueueMissed(IReadOnlyList<PendingFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        foreach (var file in files)
        {
            Enqueue(file);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Дать хосту закончить старт и миграцию БД (IDbInitializer вызывается после StartAsync).
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _optionsSubscription = options.OnChange(_ => ApplySettings());
        ApplySettings();

        var retryLoop = RunDeferredRetryLoopAsync(stoppingToken);
        var healthLoop = RunWatcherHealthLoopAsync(stoppingToken);

        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessFileAsync(item.Path, item.WatchedFolder, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Один проблемный файл не должен ронять весь воркер (ARCHITECTURE §11.3).
                    logger.LogError(ex, "Сбой при обработке файла {Path}", item.Path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста.
        }

        await Task.WhenAll(retryLoop, healthLoop).ConfigureAwait(false);
    }

    /// <summary>
    /// Периодически перекладывает «дозревшие» отложенные файлы обратно в основную очередь; файлы,
    /// исчерпавшие попытки, помечает как проблемные (ARCHITECTURE §8.6). Период читается один раз —
    /// смена настройки вступит в силу при следующем запуске.
    /// </summary>
    private async Task RunDeferredRetryLoopAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(5, options.CurrentValue.DeferredRetryPollSeconds));

        try
        {
            using var timer = new PeriodicTimer(period);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (_retryQueue.Count == 0)
                {
                    continue;
                }

                var (due, exhausted) = _retryQueue.TakeDue(options.CurrentValue, DateTimeOffset.UtcNow);

                foreach (var path in due)
                {
                    Enqueue(new PendingFile(ResolveWatchedRoot(path), path));
                }

                foreach (var path in exhausted)
                {
                    await GiveUpOnDeferredAsync(path, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Цикл ретрая отложенных файлов остановлен из-за ошибки");
        }
    }

    /// <summary>
    /// Health-check наблюдателей (ARCHITECTURE §8.5): <see cref="FileSystemWatcher"/> не переживает
    /// удаление или переименование своей папки — он тихо перестаёт слать события, оставаясь живым
    /// объектом. Раз в период сверяем ожидаемое состояние с фактическим и пересоздаём отвалившихся.
    /// </summary>
    private async Task RunWatcherHealthLoopAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(10, options.CurrentValue.WatcherHealthCheckSeconds));

        try
        {
            using var timer = new PeriodicTimer(period);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (options.CurrentValue.Enabled)
                {
                    CheckWatcherHealth();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Цикл проверки наблюдателей остановлен из-за ошибки");
        }
    }

    /// <summary>Применить настройки прямо сейчас; <c>internal</c> — тесты не поднимают весь хост.</summary>
    internal void ApplySettingsForTests() => ApplySettings();

    /// <summary>Сымитировать сбой наблюдателя — реальный <c>FileSystemWatcher</c> в тестах не ломают.</summary>
    internal void RaiseWatcherErrorForTests(string folder, Exception exception) =>
        OnWatcherError(PathComparison.Normalize(folder), new ErrorEventArgs(exception));

    /// <summary>
    /// Погасить события наблюдателя, не убирая его из словаря: так выглядит watcher, переживший
    /// пересоздание своей папки — объект жив, а события не идут (ARCHITECTURE §8.5).
    /// </summary>
    internal void DisableWatcherForTests(string folder)
    {
        lock (_watcherGate)
        {
            if (_watchers.TryGetValue(PathComparison.Normalize(folder), out var watcher))
            {
                watcher.EnableRaisingEvents = false;
            }
        }
    }

    /// <summary>Поднят ли наблюдатель за папкой и шлёт ли он события.</summary>
    internal bool IsWatcherHealthyForTests(string folder)
    {
        lock (_watcherGate)
        {
            return _watchers.TryGetValue(PathComparison.Normalize(folder), out var watcher)
                && watcher.EnableRaisingEvents;
        }
    }

    /// <summary>Один проход health-check; <c>internal</c> — тест зовёт его без реальных таймеров.</summary>
    internal void CheckWatcherHealth()
    {
        var revived = new List<string>();

        foreach (var folder in _configuredFolders)
        {
            bool healthy;
            lock (_watcherGate)
            {
                healthy = _watchers.TryGetValue(folder, out var watcher) && watcher.EnableRaisingEvents;
            }

            if (healthy || !fileSystem.DirectoryExists(folder))
            {
                continue;
            }

            logger.LogWarning("Наблюдатель за папкой {Folder} не отвечает — пересоздаём", folder);
            if (RecreateWatcher(folder))
            {
                revived.Add(folder);
            }
        }

        if (revived.Count == 0)
        {
            return;
        }

        // Пока watcher лежал, в папку могли положить файлы — досканируем, чтобы ничего не потерять.
        foreach (var folder in revived)
        {
            EnqueueFolder(folder);
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Пересоздать наблюдателей под текущие настройки и сразу просканировать папки. Вызывается при
    /// старте и на каждое изменение <c>usersettings.json</c>.
    /// </summary>
    /// <remarks>
    /// Монитор настроек срабатывает на <b>любую</b> запись файла (тумблер оформления, положение
    /// окна), причём обычно дважды. До Phase 13.10 каждый такой вызов пересоздавал наблюдателей,
    /// перечитывал все папки и поднимал шторм <see cref="StateChanged"/> → запросы к БД в UI. Теперь
    /// сравнивается снимок того, что реально влияет на наблюдение и разбор; совпал — выходим молча.
    /// </remarks>
    private void ApplySettings()
    {
        var current = options.CurrentValue;
        var folders = ResolveWatchedFolders(current);
        var snapshot = BuildSettingsSnapshot(current, folders);
        if (string.Equals(snapshot, _appliedSnapshot, StringComparison.Ordinal))
        {
            logger.LogDebug("Настройки наблюдения не изменились — наблюдатели и очередь не трогаем");
            return;
        }

        _appliedSnapshot = snapshot;
        _configuredFolders = folders;

        lock (_watcherGate)
        {
            DisposeWatchersLocked();
        }

        if (!current.Enabled || folders.Length == 0)
        {
            logger.LogInformation("Наблюдение за папками выключено");
            RaiseStateChanged();
            return;
        }

        var started = new List<string>();
        foreach (var folder in folders)
        {
            if (!fileSystem.DirectoryExists(folder))
            {
                logger.LogWarning("Наблюдаемая папка не найдена: {Folder}", folder);
                continue;
            }

            if (RecreateWatcher(folder))
            {
                started.Add(folder);
            }
        }

        logger.LogInformation(
            "Наблюдение включено для {Count} из {Total} папок: {Folders}",
            started.Count,
            folders.Length,
            string.Join(", ", started));

        foreach (var folder in started)
        {
            EnqueueFolder(folder);
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Эффективный список наблюдаемых папок (new_addons.md §4): учебная папка «одной галочкой» плюс
    /// дополнительные папки вне её. Учебная папка, которую пользователь когда-то добавил в список
    /// руками, схлопывается сама — пути нормализуются и дедуплицируются здесь же.
    /// </summary>
    /// <summary>
    /// Снимок настроек, от которых зависит наблюдение и разбор: включённость, папки (с фактом их
    /// наличия на диске — появившаяся папка обязана подхватиться следующей записью настроек, как и
    /// раньше), корень архива, игнор-лист, пороги размера/возраста, облачные заглушки.
    /// </summary>
    private string BuildSettingsSnapshot(ArchivistOptions current, string[] folders)
    {
        var parts = new List<string>
        {
            current.Enabled.ToString(),
            current.WatchStudyRoot.ToString(),
            current.ArchiveRootFolder ?? string.Empty,
            current.MinFileSizeBytes.ToString(),
            current.MinFileAgeSeconds.ToString(),
            current.SkipCloudPlaceholders.ToString(),
            string.Join("|", current.IgnoredPatterns ?? []),
        };
        parts.AddRange(folders.Select(f => f + (fileSystem.DirectoryExists(f) ? "+" : "-")));
        return string.Join("\n", parts);
    }

    private string[] ResolveWatchedFolders(ArchivistOptions current)
    {
        IEnumerable<string> configured = current.WatchedFolders ?? [];

        if (current.WatchStudyRoot && !string.IsNullOrWhiteSpace(workspace.StudyRootPath))
        {
            configured = configured.Prepend(workspace.StudyRootPath);
        }

        return [.. configured
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(PathComparison.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Поднять (или переподнять) наблюдателя ровно за одной папкой.</summary>
    private bool RecreateWatcher(string folder)
    {
        lock (_watcherGate)
        {
            if (_watchers.Remove(folder, out var old))
            {
                DisposeWatcher(old);
            }

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };

                // Папка приезжает в обработчик замыканием: FileSystemEventArgs корня наблюдения не несёт.
                watcher.Created += (_, e) => OnFileEvent(folder, e);
                watcher.Renamed += (_, e) => OnFileEvent(folder, e);
                watcher.Error += (_, e) => OnWatcherError(folder, e);
                watcher.EnableRaisingEvents = true;

                _watchers[folder] = watcher;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Не удалось поднять наблюдателя за папкой {Folder}", folder);
                return false;
            }
        }
    }

    private void OnFileEvent(string watchedFolder, FileSystemEventArgs e) =>
        Enqueue(new PendingFile(watchedFolder, e.FullPath));

    /// <summary>
    /// Сбой наблюдателя (ARCHITECTURE §8.5). Переполнение внутреннего буфера — это «мы точно потеряли
    /// события»: watcher при этом живой, пересоздавать его незачем, зато нужен немедленный проход
    /// сверки. Любая другая ошибка означает, что наблюдатель, скорее всего, мёртв — поднимаем заново.
    /// </summary>
    private void OnWatcherError(string watchedFolder, ErrorEventArgs e)
    {
        var exception = e.GetException();

        if (exception is InternalBufferOverflowException)
        {
            logger.LogWarning(
                exception,
                "Переполнен буфер наблюдателя за {Folder} — часть событий потеряна, запускаем сверку",
                watchedFolder);
            reconciliationSignal.Request($"переполнение буфера наблюдателя за {watchedFolder}");
            return;
        }

        logger.LogWarning(exception, "Сбой наблюдателя за папкой {Folder} — пересоздаём", watchedFolder);

        if (RecreateWatcher(watchedFolder))
        {
            EnqueueFolder(watchedFolder);
            RaiseStateChanged();
        }
        else
        {
            // Папка могла исчезнуть — оставляем её health-циклу, он поднимет наблюдателя, когда она вернётся.
            reconciliationSignal.Request($"наблюдатель за {watchedFolder} не поднялся");
        }
    }

    /// <summary>Разовый скан папки: подбирает файлы, о которых события не приходило.</summary>
    private void EnqueueFolder(string folder)
    {
        if (!fileSystem.DirectoryExists(folder))
        {
            return;
        }

        try
        {
            foreach (var path in fileSystem.EnumerateFiles(folder))
            {
                Enqueue(new PendingFile(folder, path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось прочитать содержимое папки {Folder}", folder);
        }
    }

    /// <summary>
    /// Дедупликация по паре (путь, окно времени): переименование на некоторых ФС приходит парой
    /// событий, да и скан папки может наложиться на событие (ARCHITECTURE §8.5).
    /// </summary>
    private void Enqueue(PendingFile file)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_recentEvents)
        {
            if (_recentEvents.TryGetValue(file.Path, out var seenAt) && now - seenAt < DuplicateEventWindow)
            {
                return;
            }

            _recentEvents[file.Path] = now;

            if (_recentEvents.Count > 512)
            {
                foreach (var stale in _recentEvents.Where(kv => now - kv.Value > DuplicateEventWindow).Select(kv => kv.Key).ToList())
                {
                    _recentEvents.Remove(stale);
                }
            }
        }

        if (!_queue.Writer.TryWrite(file))
        {
            logger.LogWarning(
                "Очередь файловых событий переполнена, {Path} будет подобран сверкой", file.Path);
        }
    }

    /// <summary>
    /// К какой из настроенных папок относится путь. Нужен там, где корень наблюдения не пришёл вместе
    /// с событием: отложенная очередь хранит только путь.
    /// </summary>
    private string ResolveWatchedRoot(string path)
    {
        foreach (var folder in _configuredFolders)
        {
            if (PathComparison.Contains(folder, path))
            {
                return folder;
            }
        }

        return Path.GetDirectoryName(path) ?? string.Empty;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Перегрузка для тестов и вызовов, где корень наблюдения выводится из настроек.</summary>
    internal Task ProcessFileAsync(string path, CancellationToken ct) =>
        ProcessFileAsync(path, ResolveWatchedRoot(path), ct);

    /// <summary>
    /// Полный разбор одного файла (ARCHITECTURE §8.2, §8.4). Файл, для которого правил не нашлось,
    /// остаётся на месте и попадает в «Неразобранное» - молча потеряться он не может.
    /// </summary>
    internal async Task ProcessFileAsync(string path, string watchedFolder, CancellationToken ct)
    {
        var current = options.CurrentValue;

        if (!fileSystem.FileExists(path))
        {
            return;
        }

        var fileName = Path.GetFileName(path);

        if (IgnoreListMatcher.IsIgnoredName(fileName, current.IgnoredPatterns))
        {
            logger.LogDebug("Файл {Name} в игнор-листе", fileName);
            return;
        }

        FileAttributes attributes;
        long size;
        DateTimeOffset createdAtUtc;
        DateTimeOffset modifiedAtUtc;
        try
        {
            attributes = fileSystem.GetAttributes(path);
            size = fileSystem.GetFileSize(path);
            createdAtUtc = fileSystem.GetCreationTimeUtc(path);
            modifiedAtUtc = fileSystem.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Файл {Path} сейчас недоступен, вернёмся к нему позже", path);
            return;
        }

        if (IgnoreListMatcher.IsSystemOrHidden(attributes))
        {
            return;
        }

        // Нематериализованный облачный файл не регистрируем вовсе: он вернётся сам, когда провайдер
        // его выкачает (ARCHITECTURE §8.5).
        if (current.SkipCloudPlaceholders && IgnoreListMatcher.IsCloudPlaceholder(attributes))
        {
            logger.LogDebug("Файл {Name} ещё не выгружен из облака - пропускаем этот заход", fileName);
            return;
        }

        if (IgnoreListMatcher.IsTooFreshAndSmall(size, createdAtUtc, DateTimeOffset.UtcNow, current))
        {
            logger.LogDebug("Файл {Name} слишком мал и свеж - вероятно, ещё пишется", fileName);
            return;
        }

        var stable = await stabilityChecker
            .WaitUntilStableAsync(path, TimeSpan.FromSeconds(current.StabilityTimeoutSeconds), ct)
            .ConfigureAwait(false);
        if (!stable)
        {
            return;
        }

        var existing = await fileRecords.GetByOriginalPathAsync(path, ct).ConfigureAwait(false);
        if (existing is not null
            && existing.Status is not (FileRecordStatus.Detected or FileRecordStatus.Deferred))
        {
            // Этот файл уже разбирали: он отсортирован, помещён в карантин или снят пользователем.
            // Deferred — не «разобрали», а «попробуем позже»: такой файл берём в работу снова.
            return;
        }

        var record = existing;
        if (record is null)
        {
            record = new FileRecord
            {
                Id = Guid.NewGuid(),
                OriginalPath = path,
                CurrentPath = path,
                ContentHash = await hasher.ComputeAsync(path, ct).ConfigureAwait(false),
                DetectedAt = DateTimeOffset.Now,
                Status = FileRecordStatus.Detected,
            };
            await fileRecords.AddAsync(record, ct).ConfigureAwait(false);
            RaiseStateChanged();
        }

        var fileInfo = new WatchedFileInfo(
            FullPath: path,
            FileName: fileName,
            Extension: Path.GetExtension(fileName).ToLowerInvariant(),
            SizeBytes: size,
            CreatedAtUtc: createdAtUtc,
            ModifiedAtUtc: modifiedAtUtc,
            WatchedFolderPath: string.IsNullOrEmpty(watchedFolder)
                ? Path.GetDirectoryName(path) ?? string.Empty
                : watchedFolder);

        var activeRules = await rules.GetActiveAsync(ct).ConfigureAwait(false);
        var decision = await engine.EvaluateAsync(fileInfo, activeRules, ct).ConfigureAwait(false);

        if (decision is null)
        {
            logger.LogInformation("Правило для {Name} не найдено - файл ждёт разбора вручную", fileName);
            RaiseStateChanged();
            return;
        }

        // Файл уже лежит ровно там, куда его отправило бы правило, и под тем же именем — двигать
        // нечего. Случай стал достижимым с Phase 13.2: учебная папка наблюдается целиком, а её
        // подпапки-предметы (куда файлы и раскладываются) могут быть в списке наблюдаемых отдельно.
        // Без этой ветки такой файл бесконечно «разбирался» бы сам в себя и оседал в карантине как
        // мнимый дубликат самого себя.
        if (PathComparison.SameDirectory(decision.TargetDirectory, Path.GetDirectoryName(path) ?? string.Empty)
            && string.Equals(decision.NewFileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            record.Status = FileRecordStatus.Sorted;
            record.CurrentPath = path;
            record.SubjectId = decision.SubjectId;
            await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);
            logger.LogDebug("Файл {Name} уже лежит в своей папке — трогать его незачем", fileName);
            RaiseStateChanged();
            return;
        }

        var recordId = record.Id;
        var result = await executor
            .MoveAndRenameAsync(decision with { SourceFileRecordId = recordId }, ct)
            .ConfigureAwait(false);

        RaiseStateChanged();

        if (result.Outcome == FileOperationOutcome.Moved)
        {
            _retryQueue.Forget(path);

            var subjectName = decision.SubjectId is { } subjectId
                ? (await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false))?.Name
                : null;
            var movedName = Path.GetFileName(result.FinalPath);

            toasts.ShowAction(
                "Файл отсортирован",
                subjectName is null
                    ? $"{movedName} — в архиве"
                    : $"{movedName} — в «{subjectName}»",
                "Отменить",
                () => UndoSortedFileAsync(recordId),
                ToastKind.Success);

            // Единственная точка семантической связи с Органайзером (ARCHITECTURE §9.3): он
            // подписан на это сообщение и сам решит, предлагать ли привязку к дедлайну.
            messenger.Send(new FileSortedMessage(
                FileRecordId: recordId,
                SubjectId: decision.SubjectId,
                FinalPath: result.FinalPath ?? string.Empty,
                FileName: movedName ?? fileName,
                SortedAt: DateTimeOffset.Now));
        }
        else if (result.Outcome == FileOperationOutcome.Deferred)
        {
            _retryQueue.Track(path, current, DateTimeOffset.UtcNow);
            logger.LogInformation("Файл {Name} занят — попробуем разобрать позже", fileName);
        }
    }

    /// <summary>Отмена только что показанной сортировки по кнопке в тосте.</summary>
    private async Task UndoSortedFileAsync(Guid fileRecordId)
    {
        bool undone;
        try
        {
            undone = await executor.UndoAsync(fileRecordId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Отмена сортировки {FileRecordId} упала", fileRecordId);
            toasts.Show("Не удалось отменить", "Во время отмены произошла ошибка.", ToastKind.Error);
            return;
        }

        RaiseStateChanged();
        if (undone)
        {
            toasts.Show("Сортировка отменена", "Файл вернулся на прежнее место.", ToastKind.Info);
        }
        else
        {
            toasts.Show("Не удалось отменить", "Файл уже перемещён вручную или прежний путь занят.", ToastKind.Warning);
        }
    }

    /// <summary>Попытки разобрать залоченный файл исчерпаны — помечаем его как проблемный.</summary>
    private async Task GiveUpOnDeferredAsync(string path, CancellationToken ct)
    {
        var record = await fileRecords.GetByOriginalPathAsync(path, ct).ConfigureAwait(false);
        if (record is null || record.Status != FileRecordStatus.Deferred)
        {
            return;
        }

        record.Status = FileRecordStatus.Quarantined;
        await fileRecords.UpdateAsync(record, ct).ConfigureAwait(false);
        RaiseStateChanged();

        toasts.Show(
            "Файл всё ещё занят",
            $"{Path.GetFileName(path)} пока не удалось разобрать — он ждёт во вкладке «Неразобранное».",
            ToastKind.Warning);
        logger.LogWarning("Отложенный файл {Path}: попытки исчерпаны, помечен как проблемный", path);
    }

    public override void Dispose()
    {
        _optionsSubscription?.Dispose();

        lock (_watcherGate)
        {
            DisposeWatchersLocked();
        }

        base.Dispose();
    }

    private void DisposeWatchersLocked()
    {
        foreach (var watcher in _watchers.Values)
        {
            DisposeWatcher(watcher);
        }

        _watchers.Clear();
    }

    private static void DisposeWatcher(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (ObjectDisposedException)
        {
            // Уже освобождён — ничего страшного.
        }

        watcher.Dispose();
    }
}
