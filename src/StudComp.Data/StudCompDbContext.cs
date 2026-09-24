using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data;

/// <summary>
/// Контекст EF Core для всей базы Rubrica (ARCHITECTURE §7). Резолвится через
/// <see cref="IDbContextFactory{TContext}"/>, а не как scoped-сервис (ARCHITECTURE §6).
/// Конфигурации сущностей — в <c>Data/Configurations</c>, здесь только набор <see cref="DbSet{TEntity}"/>.
/// </summary>
public sealed class StudCompDbContext(DbContextOptions<StudCompDbContext> options) : DbContext(options)
{
    public DbSet<Semester> Semesters => Set<Semester>();

    public DbSet<Subject> Subjects => Set<Subject>();

    public DbSet<ScheduleEntry> ScheduleEntries => Set<ScheduleEntry>();

    public DbSet<Deadline> Deadlines => Set<Deadline>();

    public DbSet<DeadlineAttachment> DeadlineAttachments => Set<DeadlineAttachment>();

    public DbSet<GradeEntry> GradeEntries => Set<GradeEntry>();

    public DbSet<ArchivistRule> ArchivistRules => Set<ArchivistRule>();

    public DbSet<FileRecord> FileRecords => Set<FileRecord>();

    public DbSet<FileOperationLogEntry> FileOperationLogs => Set<FileOperationLogEntry>();

    public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();

    public DbSet<ReportJob> ReportJobs => Set<ReportJob>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<ActivityEntry> ActivityLog => Set<ActivityEntry>();

    public DbSet<Card> Cards => Set<Card>();

    public DbSet<CardDeck> CardDecks => Set<CardDeck>();

    public DbSet<CardTag> CardTags => Set<CardTag>();

    public DbSet<CardTagLink> CardTagLinks => Set<CardTagLink>();

    public DbSet<CardReviewLog> CardReviewLogs => Set<CardReviewLog>();

    public DbSet<StudySession> StudySessions => Set<StudySession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudCompDbContext).Assembly);
    }
}
