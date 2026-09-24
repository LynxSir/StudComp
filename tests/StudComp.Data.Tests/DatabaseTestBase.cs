using Microsoft.EntityFrameworkCore;

namespace StudComp.Data.Tests;

/// <summary>
/// База для тестов слоя данных: поднимает свежий файл SQLite во временном каталоге, накатывает на него
/// миграции и убирает файл после теста. Каждый тест получает свою изолированную БД (xUnit создаёт новый
/// экземпляр класса на каждый факт).
/// </summary>
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"rubrica-test-{Guid.NewGuid():N}.db");

    // Pooling=False — чтобы соединение не удерживало файл и его можно было удалить сразу после теста.
    private string ConnectionString => $"Data Source={_databasePath};Pooling=False";

    protected DbContextOptions<StudCompDbContext> Options { get; private set; } = null!;

    protected IDbContextFactory<StudCompDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Options = new DbContextOptionsBuilder<StudCompDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        Factory = new TestDbContextFactory(Options);

        await using var context = new StudCompDbContext(Options);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    protected StudCompDbContext CreateContext() => new(Options);

    private sealed class TestDbContextFactory(DbContextOptions<StudCompDbContext> options)
        : IDbContextFactory<StudCompDbContext>
    {
        public StudCompDbContext CreateDbContext() => new(options);
    }
}
