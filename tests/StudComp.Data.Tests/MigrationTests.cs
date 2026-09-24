using Microsoft.EntityFrameworkCore;

namespace StudComp.Data.Tests;

/// <summary>
/// Проверяет, что миграция <c>InitialCreate</c> накатывается на чистый файл и создаёт ожидаемую схему
/// (PLAN.md Phase 2 DoD).
/// </summary>
public sealed class MigrationTests : DatabaseTestBase
{
    private static readonly string[] ExpectedTables =
    [
        "Subjects", "ScheduleEntries", "Deadlines", "GradeEntries",
        "ArchivistRules", "FileRecords", "FileOperationLogs", "ReportTemplates", "ReportJobs",
        "ActivityLog", "Semesters", "Notes",
        "Cards", "CardDecks", "CardTags", "CardTagLinks", "CardReviewLogs", "StudySessions",
    ];

    [Fact]
    public async Task Migration_creates_all_expected_tables()
    {
        await using var context = CreateContext();

        var tables = await context.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();

        Assert.All(ExpectedTables, expected => Assert.Contains(expected, tables));
    }

    /// <summary>
    /// Стережёт самый рискованный DDL фазы 12.3: миграция <c>AddSemester</c> пересобирает таблицу
    /// <c>Subjects</c>, снимая старую колонку <c>Semester</c> (int) в пользу FK <c>SemesterId</c>.
    /// </summary>
    [Fact]
    public async Task Subjects_table_replaced_semester_number_with_semester_link()
    {
        await using var context = CreateContext();

        var columns = await context.Database
            .SqlQuery<string>($"SELECT name AS Value FROM pragma_table_info('Subjects')")
            .ToListAsync();

        Assert.DoesNotContain("Semester", columns);
        Assert.Contains("SemesterId", columns);
    }

    [Fact]
    public async Task Migration_leaves_no_pending_changes()
    {
        await using var context = CreateContext();

        var pending = await context.Database.GetPendingMigrationsAsync();

        Assert.Empty(pending);
    }
}
