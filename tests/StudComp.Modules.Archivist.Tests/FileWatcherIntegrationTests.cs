using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Интеграционные тесты разбора файла на временных каталогах (ARCHITECTURE §12). Проверяется один шаг
/// конвейера целиком - от игнор-листа до перемещения; таймеры и реальный FileSystemWatcher в тестах
/// не задействованы, чтобы прогон был детерминированным.
/// </summary>
public sealed class FileWatcherIntegrationTests : ArchivistDatabaseTestBase, IDisposable
{
    private readonly TempFolder _watched = new("watch-src");
    private readonly TempFolder _watchedSecond = new("watch-src2");
    private readonly TempFolder _archive = new("watch-dst");
    private readonly IToastService _toasts = Substitute.For<IToastService>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly ReconciliationSignal _signal = new();

    private FileWatcherHostedService CreateWorker(bool watchBothFolders = false)
    {
        Options.CurrentValue = new ArchivistOptions
        {
            Enabled = true,
            WatchedFolders = watchBothFolders
                ? [_watched.Path, _watchedSecond.Path]
                : [_watched.Path],
            ArchiveRootFolder = _archive.Path,
            // Порог «мелкий и свежий» мешал бы коротким тестовым файлам.
            MinFileSizeBytes = 0,
            StabilityProbeIntervalMs = 20,
            StabilityTimeoutSeconds = 2,
        };

        var stabilityChecker = new FileStabilityChecker(
            FileSystem, Options, NullLogger<FileStabilityChecker>.Instance);

        return new FileWatcherHostedService(
            FileRecordRepo,
            SubjectRepo,
            Rules,
            Engine,
            Executor,
            stabilityChecker,
            Hasher,
            FileSystem,
            _toasts,
            _messenger,
            _signal,
            Workspace,
            Options,
            NullLogger<FileWatcherHostedService>.Instance);
    }

    [Fact]
    public async Task Matching_file_is_moved_to_subject_folder()
    {
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 10,
            Enabled = true,
            RenameTemplate = "{Subject}_{OriginalName}{Ext}",
        });

        var path = _watched.WriteFile("отчёт.docx", "текст отчёта");

        await CreateWorker().ProcessFileAsync(path, CancellationToken.None);

        var expected = Path.Combine(_archive.Combine("Матан"), "Матан_отчёт.docx");
        Assert.True(File.Exists(expected));
        Assert.Equal("текст отчёта", await File.ReadAllTextAsync(expected));
        Assert.False(File.Exists(path));

        // Успешная сортировка показывает тост с кнопкой «Отменить», а не простой Show.
        _toasts.Received(1).ShowAction(
            Arg.Any<string>(), Arg.Any<string>(), "Отменить", Arg.Any<Func<Task>>(), ToastKind.Success);
    }

    [Fact]
    public async Task File_without_matching_rule_stays_in_place_and_becomes_unsorted()
    {
        var path = _watched.WriteFile("архив.zip", "двоичные данные");

        await CreateWorker().ProcessFileAsync(path, CancellationToken.None);

        // Главное требование фазы: файл никуда не делся.
        Assert.True(File.Exists(path));
        Assert.Equal("двоичные данные", await File.ReadAllTextAsync(path));

        var unsorted = await FileRecordRepo.GetByStatusAsync(FileRecordStatus.Detected);
        var record = Assert.Single(unsorted);
        Assert.Equal(path, record.OriginalPath);
        _toasts.DidNotReceive().Show(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ToastKind>());
    }

    [Fact]
    public async Task Ignored_file_is_not_even_registered()
    {
        var path = _watched.WriteFile("загрузка.crdownload", "недокачано");

        await CreateWorker().ProcessFileAsync(path, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Empty(await FileRecordRepo.GetByStatusAsync(FileRecordStatus.Detected));
    }

    [Fact]
    public async Task Already_sorted_file_is_not_processed_twice()
    {
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 10,
            Enabled = true,
        });

        var path = _watched.WriteFile("отчёт.docx", "текст");
        var worker = CreateWorker();

        await worker.ProcessFileAsync(path, CancellationToken.None);

        // Тот же путь занят новым файлом с другим содержимым - повторная обработка не должна
        // ничего перезаписать в архиве.
        var again = _watched.WriteFile("отчёт.docx", "совсем другой текст");
        await worker.ProcessFileAsync(again, CancellationToken.None);

        Assert.True(File.Exists(again));
        Assert.Equal("совсем другой текст", await File.ReadAllTextAsync(again));
        Assert.Equal("текст", await File.ReadAllTextAsync(Path.Combine(_archive.Combine("Матан"), "отчёт.docx")));
    }

    [Fact]
    public async Task Locked_file_is_left_alone()
    {
        var path = _watched.WriteFile("занят.docx", "содержимое");

        var worker = CreateWorker();
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await worker.ProcessFileAsync(path, CancellationToken.None);
        }

        // Проверка стабильности не пускает залоченный файл дальше — он просто ждёт следующего скана.
        Assert.True(File.Exists(path));
        Assert.Equal("содержимое", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Deferred_record_is_picked_up_again_and_sorted()
    {
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 10,
            Enabled = true,
            RenameTemplate = "{Subject}_{OriginalName}{Ext}",
        });

        // Файл, оставшийся с прошлой попытки в состоянии Deferred (был занят во время Move).
        var path = _watched.WriteFile("отчёт.docx", "текст");
        await SeedFileRecordAsync(path, FileRecordStatus.Deferred);

        await CreateWorker().ProcessFileAsync(path, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_archive.Combine("Матан"), "Матан_отчёт.docx")));
        Assert.Equal(
            FileRecordStatus.Sorted,
            (await FileRecordRepo.GetByOriginalPathAsync(path))!.Status);
    }

    // --- Phase 10: несколько наблюдаемых папок, область действия правил, шина сообщений ---

    [Fact]
    public async Task File_from_the_second_watched_folder_is_sorted_too()
    {
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 10,
            RenameTemplate = "{Subject}_{OriginalName}{Ext}",
        });

        var path = _watchedSecond.WriteFile("отчёт.docx", "текст отчёта");

        await CreateWorker(watchBothFolders: true).ProcessFileAsync(path, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_archive.Combine("Матан"), "Матан_отчёт.docx")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Rule_scoped_to_one_folder_ignores_files_from_another()
    {
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 10,
            WatchedFolder = _watched.Path,
            RenameTemplate = "{Subject}_{OriginalName}{Ext}",
        });

        var inScope = _watched.WriteFile("свой.docx", "первый");
        var outOfScope = _watchedSecond.WriteFile("чужой.docx", "второй");

        var worker = CreateWorker(watchBothFolders: true);
        await worker.ProcessFileAsync(inScope, CancellationToken.None);
        await worker.ProcessFileAsync(outOfScope, CancellationToken.None);

        Assert.False(File.Exists(inScope));
        // Файл из другой папки правило не забрало — он цел и ждёт разбора вручную.
        Assert.True(File.Exists(outOfScope));
        Assert.Equal("второй", await File.ReadAllTextAsync(outOfScope));

        var unsorted = await FileRecordRepo.GetByStatusAsync(FileRecordStatus.Detected);
        Assert.Equal(outOfScope, Assert.Single(unsorted).OriginalPath);
    }

    [Fact]
    public async Task Rule_without_folder_scope_works_in_every_watched_folder()
    {
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 10,
            WatchedFolder = null,
            RenameTemplate = "{OriginalName}{Ext}",
        });

        var first = _watched.WriteFile("первый.docx", "a");
        var second = _watchedSecond.WriteFile("второй.docx", "b");

        var worker = CreateWorker(watchBothFolders: true);
        await worker.ProcessFileAsync(first, CancellationToken.None);
        await worker.ProcessFileAsync(second, CancellationToken.None);

        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.True(File.Exists(Path.Combine(_archive.Combine("Матан"), "первый.docx")));
        Assert.True(File.Exists(Path.Combine(_archive.Combine("Матан"), "второй.docx")));
    }

    [Fact]
    public async Task Sorted_file_is_announced_on_the_message_bus()
    {
        // Единственная точка связи с Органайзером (ARCHITECTURE §9.3): он подписан на это сообщение.
        var subject = await SeedSubjectAsync("Матан", _archive.Combine("Матан"));
        await Rules.CreateAsync(new ArchivistRule
        {
            SubjectId = subject.Id,
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            Priority = 10,
            RenameTemplate = "{Subject}_{OriginalName}{Ext}",
        });

        var received = new List<FileSortedMessage>();
        var subscriber = new object();
        _messenger.Register<FileSortedMessage>(subscriber, (_, m) => received.Add(m));

        var path = _watched.WriteFile("ЛР4.docx", "текст");
        await CreateWorker().ProcessFileAsync(path, CancellationToken.None);

        var message = Assert.Single(received);
        Assert.Equal(subject.Id, message.SubjectId);
        Assert.Equal("Матан_ЛР4.docx", message.FileName);
        Assert.Equal(Path.Combine(_archive.Combine("Матан"), "Матан_ЛР4.docx"), message.FinalPath);
        Assert.NotEqual(Guid.Empty, message.FileRecordId);

        GC.KeepAlive(subscriber);
    }

    [Fact]
    public async Task Unsorted_file_is_not_announced_on_the_message_bus()
    {
        var received = new List<FileSortedMessage>();
        var subscriber = new object();
        _messenger.Register<FileSortedMessage>(subscriber, (_, m) => received.Add(m));

        var path = _watched.WriteFile("архив.zip", "данные");
        await CreateWorker().ProcessFileAsync(path, CancellationToken.None);

        Assert.Empty(received);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public async Task Cloud_placeholder_is_skipped_and_not_registered()
    {
        // Нематериализованный OneDrive-файл читать нельзя: это спровоцирует его загрузку целиком.
        var path = _watched.WriteFile("облачный.docx", "заглушка");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline);

        try
        {
            await CreateWorker().ProcessFileAsync(path, CancellationToken.None);

            Assert.True(File.Exists(path));
            Assert.Empty(await FileRecordRepo.GetByStatusAsync(FileRecordStatus.Detected));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Cloud_placeholder_is_processed_when_the_check_is_disabled()
    {
        var path = _watched.WriteFile("облачный.docx", "заглушка");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline);

        try
        {
            var worker = CreateWorker();
            Options.CurrentValue.SkipCloudPlaceholders = false;

            await worker.ProcessFileAsync(path, CancellationToken.None);

            // Правил нет — файл остаётся на месте, но в учёт попадает.
            Assert.Equal(path, Assert.Single(
                await FileRecordRepo.GetByStatusAsync(FileRecordStatus.Detected)).OriginalPath);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    public void Dispose()
    {
        _watched.Dispose();
        _watchedSecond.Dispose();
        _archive.Dispose();
    }
}
