using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Data;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Modules.ReportForge.Services;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// База интеграционных тестов комбайна отчётов: свежий файл SQLite во временном каталоге с накатанными
/// миграциями, реальные репозитории и сервисы поверх него. Копирует подход
/// <c>StudComp.Modules.Archivist.Tests/ArchivistDatabaseTestBase</c>.
/// </summary>
public abstract class ReportForgeDatabaseTestBase : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"rubrica-rf-test-{Guid.NewGuid():N}.db");

    protected IDbContextFactory<StudCompDbContext> Factory { get; private set; } = null!;

    protected IReportTemplateRepository TemplateRepo { get; private set; } = null!;

    protected IReportJobRepository JobRepo { get; private set; } = null!;

    protected IReportTemplateService Templates { get; private set; } = null!;

    protected IGostStyleProfileProvider Profiles { get; private set; } = null!;

    protected IReportPipeline Pipeline { get; private set; } = null!;

    protected IFileSystem FileSystem { get; } = new SystemFileSystem();

    public async Task InitializeAsync()
    {
        // Pooling=False — чтобы соединение не удерживало файл и его можно было удалить сразу после теста.
        var options = new DbContextOptionsBuilder<StudCompDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        Factory = new TestDbContextFactory(options);

        await using (var context = new StudCompDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        TemplateRepo = new ReportTemplateRepository(Factory);
        JobRepo = new ReportJobRepository(Factory);

        Profiles = new GostStyleProfileProvider(TemplateRepo);
        Templates = new ReportTemplateService(TemplateRepo, JobRepo, Profiles);

        Pipeline = new ReportPipeline(
            new MarkdownDocumentModelBuilder(),
            new GostDocxRenderer(NullLogger<GostDocxRenderer>.Instance),
            Profiles,
            Templates,
            JobRepo,
            FileSystem,
            NullLogger<ReportPipeline>.Instance);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>Сколько строк в таблице шаблонов — сид обязан быть идемпотентным.</summary>
    protected async Task<int> CountTemplatesAsync()
    {
        await using var context = Factory.CreateDbContext();
        return await context.ReportTemplates.CountAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<StudCompDbContext> options)
        : IDbContextFactory<StudCompDbContext>
    {
        public StudCompDbContext CreateDbContext() => new(options);
    }
}
