using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;
using StudComp.Data;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Cards.Services.ReviewSchedulers;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// База интеграционных тестов Картотеки: свежий файл SQLite во временном каталоге с накатанными
/// миграциями, реальные репозитории и сервисы поверх него. Копирует подход
/// <c>OrganizerDatabaseTestBase</c>.
/// </summary>
/// <remarks>
/// Поиск подключается настоящий, на FTS5: у сервисов половина поведения (подсказки о дубликатах,
/// умные подборки) как раз и живёт в индексе, а подменять его моком означало бы тестировать не то.
/// </remarks>
public abstract class CardsDatabaseTestBase : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"rubrica-cards-test-{Guid.NewGuid():N}.db");

    private DbContextOptions<StudCompDbContext> _options = null!;

    protected IDbContextFactory<StudCompDbContext> Factory { get; private set; } = null!;

    protected ICardRepository CardRepo { get; private set; } = null!;

    protected ICardDeckRepository DeckRepo { get; private set; } = null!;

    protected ICardTagRepository TagRepo { get; private set; } = null!;

    protected ISubjectRepository SubjectRepo { get; private set; } = null!;

    protected ICardSearchRepository Search { get; private set; } = null!;

    protected ICardService Cards { get; private set; } = null!;

    protected ICardDeckService Decks { get; private set; } = null!;

    protected ICardTagService Tags { get; private set; } = null!;

    protected ICardReviewLogRepository ReviewLogRepo { get; private set; } = null!;

    protected IStudySessionRepository SessionRepo { get; private set; } = null!;

    protected IDeadlineRepository DeadlineRepo { get; private set; } = null!;

    protected IActivityRepository ActivityRepo { get; private set; } = null!;

    protected IReviewQueueService Queue { get; private set; } = null!;

    protected IStudySessionService Sessions { get; private set; } = null!;

    protected ICramPlanService Cram { get; private set; } = null!;

    protected ICardStatisticsService Stats { get; private set; } = null!;

    protected ICardImportExportService ImportExport { get; private set; } = null!;

    protected IFileSystem FileSystem { get; } = new SystemFileSystem();

    /// <summary>Настройки Картотеки — тесты меняют лимиты и пороги прямо в них.</summary>
    protected TestOptionsMonitor<CardsOptions> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Pooling=False — иначе соединение удержит файл и его не удалить после теста.
        _options = new DbContextOptionsBuilder<StudCompDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        Factory = new TestDbContextFactory(_options);

        await using (var context = new StudCompDbContext(_options))
        {
            await context.Database.MigrateAsync();
        }

        CardRepo = new CardRepository(Factory);
        DeckRepo = new CardDeckRepository(Factory);
        TagRepo = new CardTagRepository(Factory);
        SubjectRepo = new SubjectRepository(Factory);
        Search = new CardSearchRepository(Factory);

        ReviewLogRepo = new CardReviewLogRepository(Factory);
        SessionRepo = new StudySessionRepository(Factory);
        DeadlineRepo = new DeadlineRepository(Factory);
        ActivityRepo = new ActivityRepository(Factory);

        Tags = new CardTagService(TagRepo);
        Decks = new CardDeckService(DeckRepo, CardRepo, Search, SubjectRepo);
        Cards = new CardService(CardRepo, DeckRepo, TagRepo, Tags, Search, SubjectRepo);

        Options = new TestOptionsMonitor<CardsOptions>(new CardsOptions());

        Queue = new ReviewQueueService(CardRepo, ReviewLogRepo, Options);
        Sessions = CreateSessionService();
        Cram = new CramPlanService(CardRepo, ReviewLogRepo, DeadlineRepo, SubjectRepo, Options);
        Stats = new CardStatisticsService(CardRepo, ReviewLogRepo, TagRepo, SubjectRepo, Options);
        ImportExport = new CardImportExportService(CardRepo, DeckRepo, TagRepo, Tags, SubjectRepo, Cards, FileSystem);
    }

    /// <summary>
    /// Новый экземпляр сервиса сессий поверх той же базы. Так тест изображает перезапуск приложения:
    /// сервис состояния не держит, значит всё, что уцелело, уцелело в базе.
    /// </summary>
    protected IStudySessionService CreateSessionService() => new StudySessionService(
        CardRepo,
        DeckRepo,
        Tags,
        ReviewLogRepo,
        SessionRepo,
        SubjectRepo,
        ActivityRepo,
        new ReviewSchedulerResolver(new SingleSchedulerProvider()),
        Options);

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    protected StudCompDbContext CreateContext() => new(_options);

    /// <summary>Создать предмет в БД и вернуть его Id — точка старта большинства тестов.</summary>
    protected async Task<Guid> SeedSubjectAsync(string name = "Математический анализ", string code = "МА")
    {
        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            Name = name,
            Code = code,
            FolderPath = @"C:\Учёба\Матан",
            ColorHex = "#8E2434",
        };

        await SubjectRepo.AddAsync(subject);
        return subject.Id;
    }

    /// <summary>Создать карточку через сервис и вернуть её Id.</summary>
    protected async Task<Guid> SeedCardAsync(
        string front = "Теорема Стокса",
        string back = "Связывает поток ротора и циркуляцию.",
        Guid? subjectId = null,
        Guid? deckId = null,
        IReadOnlyList<string>? tags = null)
    {
        var result = await Cards.CreateAsync(
            new Card
            {
                Front = front,
                Back = back,
                SubjectId = subjectId,
                DeckId = deckId,
                Kind = CardKind.Term,
            },
            tags);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    /// <summary>Создать колоду через сервис и вернуть её Id.</summary>
    protected async Task<Guid> SeedDeckAsync(
        string name = "К экзамену",
        Guid? subjectId = null,
        string? query = null)
    {
        var result = await Decks.CreateAsync(new CardDeck
        {
            Name = name,
            SubjectId = subjectId,
            QueryExpression = query,
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    /// <summary>Создать карточку сразу с состоянием повторения — без неё очередь дня не проверить.</summary>
    protected async Task<Guid> SeedReviewedCardAsync(
        string front,
        double? dueInDays = null,
        double intervalDays = 6,
        double easeFactor = 2.5,
        int repetitions = 2,
        int lapses = 0,
        Guid? subjectId = null,
        Guid? deckId = null)
    {
        var id = await SeedCardAsync(front, subjectId: subjectId, deckId: deckId);

        var card = await CardRepo.GetByIdAsync(id);
        card!.DueAt = dueInDays is { } days ? DateTimeOffset.Now.AddDays(days) : null;
        card.IntervalDays = intervalDays;
        card.EaseFactor = easeFactor;
        card.Repetitions = repetitions;
        card.Lapses = lapses;
        card.LastReviewedAt = DateTimeOffset.Now.AddDays(-intervalDays);
        await CardRepo.UpdateAsync(card);

        return id;
    }

    /// <summary>Завести дедлайн-экзамен по предмету — точка входа тестов аврала.</summary>
    protected async Task<Guid> SeedExamAsync(Guid subjectId, int inDays, string title = "Экзамен")
    {
        var deadline = new Deadline
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            Title = title,
            DueDate = DateTimeOffset.Now.AddDays(inDays),
            Type = DeadlineType.Exam,
            Priority = DeadlinePriority.High,
            Status = DeadlineStatus.Pending,
        };

        await DeadlineRepo.AddAsync(deadline);
        return deadline.Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<StudCompDbContext> options)
        : IDbContextFactory<StudCompDbContext>
    {
        public StudCompDbContext CreateDbContext() => new(options);
    }

    /// <summary>
    /// Минимальный провайдер под резолвер стратегий: контейнера в тестах нет, а keyed-разрешение
    /// проверяется отдельно, в <c>ReviewSchedulerResolverTests</c>, на настоящем <c>ServiceCollection</c>.
    /// </summary>
    private sealed class SingleSchedulerProvider : IServiceProvider, IKeyedServiceProvider
    {
        private readonly Sm2ReviewScheduler _scheduler = new();

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IReviewScheduler) ? _scheduler : null;

        public object? GetKeyedService(Type serviceType, object? serviceKey) =>
            serviceType == typeof(IReviewScheduler) ? _scheduler : null;

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
            GetKeyedService(serviceType, serviceKey)
            ?? throw new InvalidOperationException($"Нет сервиса {serviceType.Name}.");
    }
}
