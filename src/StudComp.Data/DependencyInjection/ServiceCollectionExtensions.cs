using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StudComp.Core.Abstractions.Data;
using StudComp.Core.Common;
using StudComp.Data.Backup;
using StudComp.Data.Repositories;

namespace StudComp.Data.DependencyInjection;

/// <summary>
/// Точка входа слоя данных в DI-контейнер (ARCHITECTURE §6): один <c>AddXxx</c>-метод, ничего не
/// регистрируется «россыпью» снаружи.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="StudCompDbContext"/> (как фабрику, не scoped — ARCHITECTURE §6),
    /// инициализатор БД и все репозитории §7.3.
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="configuration">
    /// Конфигурация приложения. Строка подключения берётся из <c>ConnectionStrings:Rubrica</c>,
    /// при её отсутствии — база по умолчанию <c>%LocalAppData%\Rubrica\rubrica.db</c>.
    /// </param>
    public static IServiceCollection AddDataLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["ConnectionStrings:Rubrica"];
        connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? RubricaPaths.BuildSqliteConnectionString()
            : connectionString;

        services.AddDbContextFactory<StudCompDbContext>(options => options.UseSqlite(connectionString));

        services.AddSingleton<IDbInitializer, DbInitializer>();

        // Репозитории stateless (держат только фабрику контекста) — singleton безопасен и не требует
        // возни со scope в долгоживущем WPF-процессе (ARCHITECTURE §6).
        services.AddSingleton<ISemesterRepository, SemesterRepository>();
        services.AddSingleton<ISubjectRepository, SubjectRepository>();
        services.AddSingleton<IScheduleRepository, ScheduleRepository>();
        services.AddSingleton<IDeadlineRepository, DeadlineRepository>();
        services.AddSingleton<IDeadlineAttachmentRepository, DeadlineAttachmentRepository>();
        services.AddSingleton<IGradeRepository, GradeRepository>();
        services.AddSingleton<IArchivistRuleRepository, ArchivistRuleRepository>();
        services.AddSingleton<IFileRecordRepository, FileRecordRepository>();
        services.AddSingleton<IFileOperationLogRepository, FileOperationLogRepository>();
        services.AddSingleton<IReportJobRepository, ReportJobRepository>();
        services.AddSingleton<IReportTemplateRepository, ReportTemplateRepository>();
        services.AddSingleton<IActivityRepository, ActivityRepository>();
        services.AddSingleton<INoteRepository, NoteRepository>();
        services.AddSingleton<ICardRepository, CardRepository>();
        services.AddSingleton<ICardDeckRepository, CardDeckRepository>();
        services.AddSingleton<ICardTagRepository, CardTagRepository>();
        services.AddSingleton<ICardReviewLogRepository, CardReviewLogRepository>();
        services.AddSingleton<IStudySessionRepository, StudySessionRepository>();

        // Поиск по картотеке (new_addons.md §4): обе реализации живут рядом, а роутер один раз
        // проверяет наличие виртуальной таблицы и дальше отдаёт ту, что работает.
        services.AddSingleton<CardSearchRepository>();
        services.AddSingleton<FallbackCardSearchRepository>();
        services.AddSingleton<ICardSearchRepository, CardSearchRouter>();

        // Порт резервного копирования БД (new_addons.md §7 §9) — единственное место с прямым
        // Microsoft.Data.Sqlite; оркестратор IBackupService живёт в Infrastructure.
        services.AddSingleton<IDatabaseBackupPort, SqliteDatabaseBackupPort>();

        return services;
    }
}
