using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.DependencyInjection;

/// <summary>
/// Точка входа модуля «Органайзер» в DI-контейнер (ARCHITECTURE §6, §17): один <c>AddXxxModule</c>-метод.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует сервисы Органайзера (расписание, дедлайны, предметы) и фоновый планировщик
    /// напоминаний. Репозитории приходят из <c>AddDataLayer</c>, настройки — из <c>AddInfrastructure</c>.
    /// </summary>
    public static IServiceCollection AddOrganizerModule(this IServiceCollection services)
    {
        services.AddSingleton<ISemesterService, SemesterService>();
        services.AddSingleton<ISubjectService, SubjectService>();
        services.AddSingleton<INoteService, NoteService>();
        services.AddSingleton<IWorkspaceFileLedger, WorkspaceFileLedger>();
        services.AddSingleton<IScheduleService, ScheduleService>();
        services.AddSingleton<IDeadlineService, DeadlineService>();
        services.AddSingleton<IDeadlineWorkService, DeadlineWorkService>();
        services.AddSingleton<IGradeBookService, GradeBookService>();

        // Прогноз оценок — паттерн «Стратегия», keyed-регистрация (ARCHITECTURE §9.4). Резолвер
        // выбирает стратегию по Subject.ForecastStrategyName; пусто/неизвестно → средневзвешенная.
        services.AddKeyedSingleton<IGradeForecastStrategy, WeightedAverageForecastStrategy>(
            WeightedAverageForecastStrategy.Key);
        services.AddKeyedSingleton<IGradeForecastStrategy, LinearRegressionForecastStrategy>(
            LinearRegressionForecastStrategy.Key);
        services.AddSingleton<IGradeForecastStrategyResolver, GradeForecastStrategyResolver>();

        // Один экземпляр обслуживает и роль INotificationScheduler (RequestReschedule из VM),
        // и роль IHostedService (жизненный цикл хоста).
        services.AddSingleton<NotificationSchedulerHostedService>();
        services.AddSingleton<INotificationScheduler>(sp =>
            sp.GetRequiredService<NotificationSchedulerHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<NotificationSchedulerHostedService>());

        // Подписка на FileSortedMessage от Архивариуса (ARCHITECTURE §9.3). IHostedService нужен ради
        // момента создания: ленивый singleton никто бы не сконструировал, и подписки бы не было.
        services.AddSingleton<DeadlineLinkSuggestionService>();
        services.AddHostedService(sp => sp.GetRequiredService<DeadlineLinkSuggestionService>());

        return services;
    }
}
