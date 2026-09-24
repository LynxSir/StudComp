using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Периодическая сверка «диск ↔ учёт» (ARCHITECTURE §8.2, §8.5). Ключевой сценарий DoD Phase 10:
/// пачка файлов, о которой <c>FileSystemWatcher</c> не сообщил, не теряется. Второй фронт — разбор
/// записей журнала, зависших в <c>Planned</c> после падения между журналом и <c>File.Move</c>.
/// </summary>
/// <remarks>
/// Сверка не имеет права двигать или удалять файлы (ARCHITECTURE §14), поэтому почти каждый тест
/// заканчивается сверкой снимка папок до и после прохода.
/// </remarks>
public sealed class ReconciliationServiceTests : ArchivistDatabaseTestBase, IDisposable
{
    private readonly TempFolder _watched = new("recon-src");
    private readonly TempFolder _archive = new("recon-dst");
    private readonly TempFolder _watched2 = new("recon-src2");
    private readonly TempFolder _watched3 = new("recon-src3");
    private readonly IFileWatcherService _watcher = Substitute.For<IFileWatcherService>();

    private readonly List<PendingFile> _enqueued = [];

    private ReconciliationHostedService CreateService(int stalePlannedMinutes = 5)
        => CreateServiceOver([_watched.Path], stalePlannedMinutes);

    private ReconciliationHostedService CreateServiceOver(
        IReadOnlyList<string> folders, int stalePlannedMinutes = 5)
    {
        Options.CurrentValue = new ArchivistOptions
        {
            Enabled = true,
            WatchedFolders = [.. folders],
            ArchiveRootFolder = _archive.Path,
            MinFileSizeBytes = 0,
            ReconciliationStalePlannedMinutes = stalePlannedMinutes,
        };

        _watcher.WatchedFolders.Returns(folders);
        _watcher
            .When(w => w.EnqueueMissed(Arg.Any<IReadOnlyList<PendingFile>>()))
            .Do(call => _enqueued.AddRange(call.Arg<IReadOnlyList<PendingFile>>()));

        return new ReconciliationHostedService(
            _watcher,
            FileRecordRepo,
            OperationLogRepo,
            FileSystem,
            new ReconciliationSignal(),
            Options,
            NullLogger<ReconciliationHostedService>.Instance);
    }

    [Fact]
    public async Task File_the_watcher_never_saw_is_queued_for_processing()
    {
        var path = _watched.WriteFile("отчёт.docx", "содержимое");

        var report = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.Scanned);
        Assert.Equal(1, report.Missed);
        Assert.Equal(path, Assert.Single(_enqueued).Path);
        Assert.Equal(_watched.Path, _enqueued[0].WatchedFolder);
    }

    [Fact]
    public async Task Already_sorted_file_is_not_queued_again()
    {
        var path = _watched.WriteFile("отчёт.docx", "содержимое");
        await SeedFileRecordAsync(path, FileRecordStatus.Sorted);

        var report = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.Scanned);
        Assert.Equal(0, report.Missed);
        Assert.Empty(_enqueued);
    }

    [Theory]
    [InlineData(FileRecordStatus.Detected)]
    [InlineData(FileRecordStatus.Deferred)]
    public async Task Unfinished_record_is_picked_up_again(FileRecordStatus status)
    {
        // Detected и Deferred — это «ещё не разобрали», а не «разобрали»: такие файлы берём снова.
        var path = _watched.WriteFile("отчёт.docx", "содержимое");
        await SeedFileRecordAsync(path, status);

        var report = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.Missed);
        Assert.Equal(path, Assert.Single(_enqueued).Path);
    }

    [Fact]
    public async Task Ignored_and_hidden_files_are_skipped()
    {
        _watched.WriteFile("черновик.tmp", "мусор");
        var hidden = _watched.WriteFile("скрытый.docx", "содержимое");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var good = _watched.WriteFile("отчёт.docx", "содержимое");

        var report = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(3, report.Scanned);
        Assert.Equal(1, report.Missed);
        Assert.Equal(good, Assert.Single(_enqueued).Path);
    }

    [Fact]
    public async Task Disabled_archivist_scans_nothing()
    {
        _watched.WriteFile("отчёт.docx", "содержимое");
        var service = CreateService();
        Options.CurrentValue.Enabled = false;

        var report = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(default, report);
        Assert.Empty(_enqueued);
    }

    /// <summary>
    /// Главный пункт DoD Phase 10: массовое копирование переполняет внутренний буфер наблюдателя, но
    /// ни один файл не теряется — следующий проход сверки подбирает всю пачку.
    /// </summary>
    [Fact]
    public async Task Bulk_copy_of_hundreds_of_files_is_fully_recovered()
    {
        const int count = 300;
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            expected.Add(_watched.WriteFile($"лекция{i:D3}.pdf", $"содержимое {i}"));
        }

        var service = CreateService();
        var report = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(count, report.Scanned);
        Assert.Equal(count, report.Missed);
        Assert.Equal(expected, _enqueued.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));

        // Второй проход не должен ничего делать заново - файлы к тому моменту уже в учёте.
        foreach (var path in expected)
        {
            await SeedFileRecordAsync(path, FileRecordStatus.Sorted);
        }

        _enqueued.Clear();
        var second = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, second.Missed);
        Assert.Empty(_enqueued);
    }

    /// <summary>
    /// Тестовая закалка Phase 12: тот же сценарий, что и DoD Phase 10, но объём варьируется и явно
    /// проверяются инвариант §14 (снимок папки до/после совпадает) и отсутствие дублей в очереди.
    /// </summary>
    [Theory]
    [InlineData(300)]
    [InlineData(750)]
    public async Task Bulk_copy_of_any_volume_is_recovered_in_one_pass_without_touching_disk(int count)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            expected.Add(_watched.WriteFile($"лекция{i:D4}.pdf", $"содержимое {i}"));
        }

        var before = Snapshot();
        var service = CreateService();
        var report = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(count, report.Scanned);
        Assert.Equal(count, report.Missed);

        var queued = _enqueued.Select(f => f.Path).ToList();
        Assert.Equal(count, queued.Count);                       // без дублей в очереди
        Assert.Equal(count, queued.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(expected, queued.ToHashSet(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(before, Snapshot());                        // §14: ни один файл не сдвинут

        // Учёт пополнился — второй проход обязан быть пустым.
        foreach (var path in expected)
        {
            await SeedFileRecordAsync(path, FileRecordStatus.Sorted);
        }

        _enqueued.Clear();
        var second = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, second.Missed);
        Assert.Empty(_enqueued);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>
    /// Пачка «размазана» по нескольким наблюдаемым папкам (Phase 10 сделал их множественными) —
    /// сверка обходит все и относит каждый файл к его корню наблюдения.
    /// </summary>
    [Fact]
    public async Task Bulk_copy_spread_across_folders_is_fully_recovered()
    {
        var folders = new[] { _watched.Path, _watched2.Path, _watched3.Path };
        var perFolder = 200;
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (folder, index) in folders.Select((f, i) => (f, i)))
        {
            for (var i = 0; i < perFolder; i++)
            {
                var path = Path.Combine(folder, $"файл{index}_{i:D3}.docx");
                File.WriteAllText(path, $"папка {index}, файл {i}");
                expected[path] = folder;
            }
        }

        var before = Snapshot();
        var report = await CreateServiceOver(folders).RunOnceAsync(CancellationToken.None);

        Assert.Equal(folders.Length * perFolder, report.Scanned);
        Assert.Equal(folders.Length * perFolder, report.Missed);

        // Каждый файл поставлен в очередь ровно один раз и с правильным корнем наблюдения.
        var byPath = _enqueued.ToDictionary(f => f.Path, f => f.WatchedFolder, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected.Count, byPath.Count);
        foreach (var (path, folder) in expected)
        {
            Assert.Equal(folder, byPath[path]);
        }

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Stale_planned_entry_is_completed_when_the_file_reached_its_target()
    {
        // Падение случилось после Move, но до закрытия журнала: файл лежит в цели, запись в Planned.
        var original = Path.Combine(_watched.Path, "отчёт.docx");
        var planned = _archive.WriteFile("Матан_отчёт.docx", "содержимое");
        var record = await SeedFileRecordAsync(planned);
        record.OriginalPath = original;
        await FileRecordRepo.UpdateAsync(record);
        await SeedPlannedEntryAsync(record.Id, original, planned, ageMinutes: 30);

        var before = Snapshot();
        var report = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.Recovered);
        Assert.Equal(0, report.Quarantined);

        var entry = Assert.Single(await OperationLogRepo.GetByFileRecordAsync(record.Id));
        Assert.Equal(FileOperationLogStatus.Completed, entry.Status);
        Assert.Equal(planned, entry.FinalPath);

        var updated = await FileRecordRepo.GetByIdAsync(record.Id);
        Assert.Equal(FileRecordStatus.Sorted, updated!.Status);
        Assert.Equal(planned, updated.CurrentPath);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Stale_planned_entry_is_failed_when_the_file_never_moved()
    {
        // Падение случилось до Move: файл остался на месте, запись должна вернуться в «Обнаружен».
        var original = _watched.WriteFile("отчёт.docx", "содержимое");
        var planned = Path.Combine(_archive.Path, "Матан_отчёт.docx");
        var record = await SeedFileRecordAsync(original);
        await SeedPlannedEntryAsync(record.Id, original, planned, ageMinutes: 30);

        var before = Snapshot();
        var report = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.Recovered);

        var entry = Assert.Single(await OperationLogRepo.GetByFileRecordAsync(record.Id));
        Assert.Equal(FileOperationLogStatus.Failed, entry.Status);

        var updated = await FileRecordRepo.GetByIdAsync(record.Id);
        Assert.Equal(FileRecordStatus.Detected, updated!.Status);
        Assert.Equal(original, updated.CurrentPath);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Stale_planned_entry_is_quarantined_when_the_file_vanished()
    {
        // Файла нет ни там, ни там — его унесли мимо нас. Ничего не выдумываем, помечаем проблемным.
        var original = Path.Combine(_watched.Path, "пропал.docx");
        var planned = Path.Combine(_archive.Path, "Матан_пропал.docx");
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            OriginalPath = original,
            CurrentPath = original,
            ContentHash = "hash",
            DetectedAt = DateTimeOffset.Now,
            Status = FileRecordStatus.Detected,
        };
        await FileRecordRepo.AddAsync(record);
        await SeedPlannedEntryAsync(record.Id, original, planned, ageMinutes: 30);

        var report = await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, report.Recovered);
        Assert.Equal(1, report.Quarantined);

        var entry = Assert.Single(await OperationLogRepo.GetByFileRecordAsync(record.Id));
        Assert.Equal(FileOperationLogStatus.Failed, entry.Status);
        Assert.Equal(FileRecordStatus.Quarantined, (await FileRecordRepo.GetByIdAsync(record.Id))!.Status);
    }

    [Fact]
    public async Task Fresh_planned_entry_is_left_alone()
    {
        // Операция могла начаться секунду назад и идти прямо сейчас — вмешиваться нельзя.
        var original = _watched.WriteFile("отчёт.docx", "содержимое");
        var planned = Path.Combine(_archive.Path, "Матан_отчёт.docx");
        var record = await SeedFileRecordAsync(original);
        await SeedPlannedEntryAsync(record.Id, original, planned, ageMinutes: 0);

        var report = await CreateService(stalePlannedMinutes: 5).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, report.Recovered);
        Assert.Equal(0, report.Quarantined);

        var entry = Assert.Single(await OperationLogRepo.GetByFileRecordAsync(record.Id));
        Assert.Equal(FileOperationLogStatus.Planned, entry.Status);
    }

    /// <summary>Инвариант §14: сверка читает диск и правит учёт, но файлов не касается.</summary>
    [Fact]
    public async Task Reconciliation_never_touches_files_on_disk()
    {
        _watched.WriteFile("отчёт.docx", "первый");
        _watched.WriteFile("лекция.pdf", "второй");
        _archive.WriteFile("уже-в-архиве.docx", "третий");

        var before = Snapshot();
        await CreateService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(before, Snapshot());
    }

    /// <summary>Пути и содержимое всех задействованных папок — для проверки «ничего не сдвинулось».</summary>
    private Dictionary<string, string> Snapshot() =>
        new[] { _watched.Path, _watched2.Path, _watched3.Path, _archive.Path }
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .ToDictionary(path => path, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    private async Task SeedPlannedEntryAsync(
        Guid fileRecordId, string originalPath, string plannedPath, int ageMinutes)
    {
        await OperationLogRepo.AddAsync(new FileOperationLogEntry
        {
            Id = Guid.NewGuid(),
            FileRecordId = fileRecordId,
            OriginalPath = originalPath,
            PlannedPath = plannedPath,
            StartedAt = DateTimeOffset.Now.AddMinutes(-ageMinutes),
            Status = FileOperationLogStatus.Planned,
        });
    }

    public void Dispose()
    {
        _watched.Dispose();
        _archive.Dispose();
        _watched2.Dispose();
        _watched3.Dispose();
    }
}
