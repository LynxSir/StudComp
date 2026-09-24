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
/// Наблюдение за учебной папкой «одной галочкой» (Phase 13.2, new_addons.md §4): указанную один раз
/// учебную папку не нужно дублировать в списке наблюдаемых.
/// </summary>
public sealed class StudyRootWatchingTests : ArchivistDatabaseTestBase, IDisposable
{
    private readonly TempFolder _studyRoot = new("sr-root");
    private readonly TempFolder _downloads = new("sr-downloads");
    private readonly ReconciliationSignal _signal = new();
    private readonly IToastService _toasts = Substitute.For<IToastService>();

    private FileWatcherHostedService CreateWorker(bool watchStudyRoot, params string[] extraFolders)
    {
        WorkspaceOptions.CurrentValue = new WorkspaceOptions { StudyRootPath = _studyRoot.Path };
        Options.CurrentValue = new ArchivistOptions
        {
            Enabled = true,
            WatchStudyRoot = watchStudyRoot,
            WatchedFolders = [.. extraFolders],
            MinFileSizeBytes = 0,
            StabilityProbeIntervalMs = 20,
            StabilityTimeoutSeconds = 2,
        };

        return new FileWatcherHostedService(
            FileRecordRepo,
            SubjectRepo,
            Rules,
            Engine,
            Executor,
            new FileStabilityChecker(FileSystem, Options, NullLogger<FileStabilityChecker>.Instance),
            Hasher,
            FileSystem,
            _toasts,
            new WeakReferenceMessenger(),
            _signal,
            Workspace,
            Options,
            NullLogger<FileWatcherHostedService>.Instance);
    }

    [Fact]
    public void Study_root_is_watched_without_listing_it_manually()
    {
        var worker = CreateWorker(watchStudyRoot: true);

        worker.ApplySettingsForTests();

        Assert.Equal([_studyRoot.Path], worker.WatchedFolders);
    }

    [Fact]
    public void Study_root_is_not_watched_when_the_toggle_is_off()
    {
        var worker = CreateWorker(watchStudyRoot: false, _downloads.Path);

        worker.ApplySettingsForTests();

        Assert.Equal([_downloads.Path], worker.WatchedFolders);
    }

    [Fact]
    public void Extra_folders_live_alongside_the_study_root()
    {
        var worker = CreateWorker(watchStudyRoot: true, _downloads.Path);

        worker.ApplySettingsForTests();

        Assert.Equal(
            new[] { _downloads.Path, _studyRoot.Path }.OrderBy(p => p),
            worker.WatchedFolders.OrderBy(p => p));
    }

    /// <summary>
    /// Учебную папку, добавленную в список руками ещё до Phase 13.2, галочка не должна задваивать —
    /// иначе на одну папку поднялось бы два наблюдателя и каждый файл разбирался бы дважды.
    /// </summary>
    [Fact]
    public void Manually_listed_study_root_is_not_watched_twice()
    {
        var worker = CreateWorker(watchStudyRoot: true, _studyRoot.Path + Path.DirectorySeparatorChar);

        worker.ApplySettingsForTests();

        Assert.Single(worker.WatchedFolders);
    }

    /// <summary>Настроенные папки видны интерфейсу даже до того, как наблюдатели реально подняты.</summary>
    [Fact]
    public void Configured_folders_include_the_study_root_for_the_rule_editor()
    {
        var worker = CreateWorker(watchStudyRoot: true, _downloads.Path);

        worker.ApplySettingsForTests();

        Assert.Contains(_studyRoot.Path, worker.ConfiguredFolders);
        Assert.Contains(_downloads.Path, worker.ConfiguredFolders);
    }

    /// <summary>
    /// Ключевой инвариант §14: файл, уже разложенный в подпапку предмета внутри учебной папки, не
    /// попадает под наблюдение повторно — иначе возник бы цикл «разложил → снова увидел → переложил».
    /// Гарантия конструктивная: наблюдение нерекурсивное, корень работает «входящими».
    /// </summary>
    [Fact]
    public async Task A_file_already_sorted_into_a_subject_folder_is_not_picked_up_again()
    {
        var subject = await SeedSubjectAsync("Матан");
        var worker = CreateWorker(watchStudyRoot: true);
        await Rules.CreateAsync(new ArchivistRule
        {
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            SubjectId = subject.Id,
            Enabled = true,
            Priority = 10,
        });

        // Файл кладут в корень учебной папки — это и есть «входящие».
        var incoming = Path.Combine(_studyRoot.Path, "лекция.docx");
        await System.IO.File.WriteAllTextAsync(incoming, "конспект");

        await worker.ProcessFileAsync(incoming, _studyRoot.Path, CancellationToken.None);

        var sorted = Directory.GetFiles(Path.Combine(_studyRoot.Path, "Матан"));
        Assert.Single(sorted);
        Assert.False(System.IO.File.Exists(incoming));

        // Повторный обход корня видит только его верхний уровень — разложенного файла там уже нет.
        var rootLevel = FileSystem.EnumerateFiles(_studyRoot.Path).ToList();
        Assert.Empty(rootLevel);
        Assert.DoesNotContain(sorted[0], rootLevel);
    }

    /// <summary>
    /// Продолжение того же инварианта для конфигураций, доставшихся от 1.0.0: подпапка предмета может
    /// стоять в списке наблюдаемых отдельно. Файл, который уже лежит в своей папке, не должен
    /// двигаться, попадать в карантин как «дубликат самого себя» и мелькать в «Неразобранном».
    /// </summary>
    [Fact]
    public async Task A_file_in_a_separately_watched_subject_folder_is_left_alone()
    {
        var subjectFolder = Path.Combine(_studyRoot.Path, "Матан");
        Directory.CreateDirectory(subjectFolder);
        var subject = await SeedSubjectAsync("Матан");

        var worker = CreateWorker(watchStudyRoot: true, subjectFolder);
        await Rules.CreateAsync(new ArchivistRule
        {
            Pattern = ".docx",
            MatchType = RuleMatchType.Extension,
            SubjectId = subject.Id,
            Enabled = true,
            Priority = 10,
        });

        var inPlace = Path.Combine(subjectFolder, "лекция.docx");
        await System.IO.File.WriteAllTextAsync(inPlace, "конспект");

        await worker.ProcessFileAsync(inPlace, subjectFolder, CancellationToken.None);

        // Файл на месте, и он единственный — копии с суффиксом « (2)» не появилось.
        Assert.True(System.IO.File.Exists(inPlace));
        Assert.Single(Directory.GetFiles(subjectFolder));
        Assert.Equal("конспект", await System.IO.File.ReadAllTextAsync(inPlace));

        // И он не числится ожидающим разбора.
        var record = await FileRecordRepo.GetByOriginalPathAsync(inPlace);
        Assert.NotNull(record);
        Assert.Equal(FileRecordStatus.Sorted, record.Status);
    }

    public void Dispose()
    {
        _studyRoot.Dispose();
        _downloads.Dispose();
    }
}
