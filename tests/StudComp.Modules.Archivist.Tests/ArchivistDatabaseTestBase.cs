using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Data;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;
using StudComp.Infrastructure.Workspace;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// База интеграционных тестов Архивариуса: свежий файл SQLite во временном каталоге с накатанными
/// миграциями, реальные репозитории и сервисы поверх него. Копирует подход
/// <c>StudComp.Modules.Organizer.Tests/OrganizerDatabaseTestBase</c>.
/// </summary>
public abstract class ArchivistDatabaseTestBase : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"rubrica-arc-test-{Guid.NewGuid():N}.db");

    protected TestOptionsMonitor<ArchivistOptions> Options { get; } = new(new ArchivistOptions());

    /// <summary>Учебная папка (Phase 13.2) — из неё считается цель сортировки, когда корень архива пуст.</summary>
    protected TestOptionsMonitor<WorkspaceOptions> WorkspaceOptions { get; } = new(new WorkspaceOptions());

    protected IDbContextFactory<StudCompDbContext> Factory { get; private set; } = null!;

    protected ISubjectRepository SubjectRepo { get; private set; } = null!;

    protected IArchivistRuleRepository RuleRepo { get; private set; } = null!;

    protected IFileRecordRepository FileRecordRepo { get; private set; } = null!;

    protected IFileOperationLogRepository OperationLogRepo { get; private set; } = null!;

    protected IFileSystem FileSystem { get; } = new SystemFileSystem();

    protected IStudyWorkspace Workspace { get; private set; } = null!;

    /// <summary>Резолвер целевой папки предмета — один на движок правил и ручную сортировку.</summary>
    internal SubjectTargetResolver Targets { get; private set; } = null!;

    protected IFileHasher Hasher { get; private set; } = null!;

    protected IArchivistRuleService Rules { get; private set; } = null!;

    protected ISortingRuleEngine Engine { get; private set; } = null!;

    protected IFileOperationExecutor Executor { get; private set; } = null!;

    protected IOperationHistoryService History { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StudCompDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        Factory = new TestDbContextFactory(options);

        await using (var context = new StudCompDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        SubjectRepo = new SubjectRepository(Factory);
        RuleRepo = new ArchivistRuleRepository(Factory);
        FileRecordRepo = new FileRecordRepository(Factory);
        OperationLogRepo = new FileOperationLogRepository(Factory);

        Hasher = new FileHasher(FileSystem);
        Rules = new ArchivistRuleService(RuleRepo, SubjectRepo, FileSystem);
        Workspace = new StudyWorkspace(WorkspaceOptions, FileSystem);
        Targets = new SubjectTargetResolver(Workspace, Options);
        Engine = new SortingRuleEngine(SubjectRepo, Options, Targets, NullLogger<SortingRuleEngine>.Instance);
        Executor = new FileOperationExecutor(
            FileSystem,
            Hasher,
            FileRecordRepo,
            OperationLogRepo,
            NullLogger<FileOperationExecutor>.Instance);
        History = new OperationHistoryService(OperationLogRepo, Executor, FileSystem);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>Создать предмет в БД и вернуть его — точка старта для большинства тестов.</summary>
    protected async Task<Subject> SeedSubjectAsync(string name = "Матан", string folderPath = "")
    {
        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            Name = name,
            Code = "МА",
            FolderPath = folderPath,
        };
        await SubjectRepo.AddAsync(subject);
        return subject;
    }

    /// <summary>Завести запись учёта для реально существующего файла.</summary>
    protected async Task<FileRecord> SeedFileRecordAsync(
        string path, FileRecordStatus status = FileRecordStatus.Detected)
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            OriginalPath = path,
            CurrentPath = path,
            ContentHash = await Hasher.ComputeAsync(path),
            DetectedAt = DateTimeOffset.Now,
            Status = status,
        };
        await FileRecordRepo.AddAsync(record);
        return record;
    }

    private sealed class TestDbContextFactory(DbContextOptions<StudCompDbContext> options)
        : IDbContextFactory<StudCompDbContext>
    {
        public StudCompDbContext CreateDbContext() => new(options);
    }
}
