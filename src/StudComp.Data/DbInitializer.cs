using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StudComp.Core.Common;

namespace StudComp.Data;

/// <inheritdoc cref="IDbInitializer"/>
internal sealed class DbInitializer(
    IDbContextFactory<StudCompDbContext> contextFactory,
    ILogger<DbInitializer> logger) : IDbInitializer
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(RubricaPaths.DataDirectory);

        await using var context = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var pending = (await context.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).ToArray();
        if (pending.Length == 0)
        {
            logger.LogInformation("База актуальна, миграций к применению нет");
            return;
        }

        logger.LogInformation("Применяю миграции ({Count}): {Migrations}", pending.Length, string.Join(", ", pending));
        await context.Database.MigrateAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Миграции применены, база готова: {Path}", RubricaPaths.DatabaseFile);
    }
}
