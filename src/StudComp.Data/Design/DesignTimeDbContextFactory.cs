using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using StudComp.Core.Common;

namespace StudComp.Data.Design;

/// <summary>
/// Фабрика контекста для <c>dotnet ef</c> на этапе разработки. Позволяет запускать миграции командой
/// <c>dotnet ef migrations add &lt;Name&gt; --project src/StudComp.Data</c> без <c>--startup-project</c>:
/// в <c>StudComp.App</c> хоста ещё нет (он появится в Phase 3, ARCHITECTURE §6), а инструменту нужен
/// способ собрать <see cref="StudCompDbContext"/>.
/// </summary>
/// <remarks>
/// Использует отдельный файл во временном каталоге, не боевой <c>%LocalAppData%\Rubrica\rubrica.db</c> —
/// генерация миграций не должна трогать реальную базу. В рантайме контекст поднимается через
/// <c>AddDataLayer</c>, эта фабрика туда не попадает.
/// </remarks>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StudCompDbContext>
{
    public StudCompDbContext CreateDbContext(string[] args)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "rubrica-design.db");

        var options = new DbContextOptionsBuilder<StudCompDbContext>()
            .UseSqlite(RubricaPaths.BuildSqliteConnectionString(databasePath))
            .Options;

        return new StudCompDbContext(options);
    }
}
