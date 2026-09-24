using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Domain;
using StudComp.Data;
using StudComp.Data.Repositories;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// База интеграционных тестов Органайзера: свежий файл SQLite во временном каталоге с накатанными
/// миграциями, реальные репозитории и сервисы поверх него (PLAN.md Phase 4 — «CRUD через temp-SQLite»).
/// Копирует подход <c>StudComp.Data.Tests/DatabaseTestBase</c>.
/// </summary>
public abstract class OrganizerDatabaseTestBase : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"rubrica-org-test-{Guid.NewGuid():N}.db");

    private DbContextOptions<StudCompDbContext> _options = null!;

    protected IDbContextFactory<StudCompDbContext> Factory { get; private set; } = null!;

    protected ISubjectRepository SubjectRepo { get; private set; } = null!;

    protected IScheduleRepository ScheduleRepo { get; private set; } = null!;

    protected IDeadlineRepository DeadlineRepo { get; private set; } = null!;

    protected IGradeRepository GradeRepo { get; private set; } = null!;

    /// <summary>Нужен привязке файла к дедлайну (ARCHITECTURE §9.3, Phase 10).</summary>
    protected IFileRecordRepository FileRecordRepo { get; private set; } = null!;

    protected ISemesterRepository SemesterRepo { get; private set; } = null!;

    protected INoteRepository NoteRepo { get; private set; } = null!;

    protected ISubjectService Subjects { get; private set; } = null!;

    protected IScheduleService Schedule { get; private set; } = null!;

    protected IDeadlineService Deadlines { get; private set; } = null!;

    protected IGradeBookService Grades { get; private set; } = null!;

    protected ISemesterService Semesters { get; private set; } = null!;

    protected INoteService Notes { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _options = new DbContextOptionsBuilder<StudCompDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        Factory = new TestDbContextFactory(_options);

        await using (var context = new StudCompDbContext(_options))
        {
            await context.Database.MigrateAsync();
        }

        SubjectRepo = new SubjectRepository(Factory);
        ScheduleRepo = new ScheduleRepository(Factory);
        DeadlineRepo = new DeadlineRepository(Factory);
        GradeRepo = new GradeRepository(Factory);
        FileRecordRepo = new FileRecordRepository(Factory);
        SemesterRepo = new SemesterRepository(Factory);
        NoteRepo = new NoteRepository(Factory);
        Subjects = new SubjectService(SubjectRepo);
        Semesters = new SemesterService(SemesterRepo);
        Notes = new NoteService(NoteRepo, SubjectRepo);
        Schedule = new ScheduleService(ScheduleRepo, SubjectRepo);
        Deadlines = new DeadlineService(DeadlineRepo, SubjectRepo, FileRecordRepo);
        Grades = new GradeBookService(
            GradeRepo,
            SubjectRepo,
            new TestStrategyResolver(),
            NullLogger<GradeBookService>.Instance);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>Создать предмет в БД и вернуть его Id — точка старта для большинства тестов.</summary>
    protected async Task<Guid> SeedSubjectAsync(string name = "Матан", Guid? semesterId = null)
    {
        var result = await Subjects.CreateAsync(
            new Subject { Name = name, Code = "МА", SemesterId = semesterId });
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>Создать активный семестр и вернуть его Id.</summary>
    protected async Task<Guid> SeedSemesterAsync(
        DateOnly? start = null,
        DateOnly? end = null,
        bool firstWeekIsOdd = true,
        string name = "Тестовый семестр")
    {
        var result = await Semesters.CreateAsync(new Semester
        {
            Name = name,
            CourseNumber = 1,
            StartDate = start ?? new DateOnly(2026, 9, 1),
            EndDate = end,
            FirstWeekIsOdd = firstWeekIsOdd,
            IsActive = true,
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    /// <summary>Создать запись учёта файла — заготовка для тестов привязки к дедлайну.</summary>
    protected async Task<Guid> SeedFileRecordAsync(string path = @"C:\Архив\ЛР4_Матан.docx")
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            OriginalPath = path,
            CurrentPath = path,
            ContentHash = "hash",
            DetectedAt = DateTimeOffset.Now,
            Status = FileRecordStatus.Sorted,
        };

        await FileRecordRepo.AddAsync(record);
        return record.Id;
    }

    /// <summary>Создать оценку (или запланированную аттестацию) по предмету и вернуть её Id.</summary>
    protected async Task<Guid> SeedGradeAsync(
        Guid subjectId,
        decimal raw,
        decimal max,
        decimal weight,
        bool planned = false,
        DateTime? date = null,
        string title = "Аттестация")
    {
        var result = await Grades.CreateAsync(new GradeEntry
        {
            SubjectId = subjectId,
            Type = GradeEntryType.Attestation,
            Title = title,
            IsPlanned = planned,
            RawScore = raw,
            MaxScore = max,
            Weight = weight,
            Date = date ?? DateTime.Today,
            Semester = 1,
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private sealed class TestDbContextFactory(DbContextOptions<StudCompDbContext> options)
        : IDbContextFactory<StudCompDbContext>
    {
        public StudCompDbContext CreateDbContext() => new(options);
    }

    /// <summary>
    /// Резолвер для тестов: маршрутизирует по ключу между настоящими стратегиями модуля
    /// (пусто/неизвестно → средневзвешенная), без DI-контейнера.
    /// </summary>
    private sealed class TestStrategyResolver : IGradeForecastStrategyResolver
    {
        private readonly WeightedAverageForecastStrategy _weighted = new();
        private readonly LinearRegressionForecastStrategy _linear = new();

        public IGradeForecastStrategy Resolve(string? name) => name switch
        {
            LinearRegressionForecastStrategy.Key => _linear,
            _ => _weighted,
        };
    }
}
