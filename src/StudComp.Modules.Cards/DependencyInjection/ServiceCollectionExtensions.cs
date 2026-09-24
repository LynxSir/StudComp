using Microsoft.Extensions.DependencyInjection;
using StudComp.Core.Abstractions.Cards;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Cards.Services.ReviewSchedulers;

namespace StudComp.Modules.Cards.DependencyInjection;

/// <summary>
/// Точка входа модуля «Картотека» в DI-контейнер (ARCHITECTURE §6, §17): один
/// <c>AddXxxModule</c>-метод, ничего не регистрируется «россыпью» снаружи.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует сервисы картотеки: карточки, колоды, метки, очередь повторения, сессии
    /// тренажёра, аврал, статистику и напоминание. Репозитории и поисковый индекс приходят из
    /// <c>AddDataLayer</c>, настройки (<c>CardsOptions</c>) — из <c>AddInfrastructure</c>.
    /// </summary>
    public static IServiceCollection AddCardsModule(this IServiceCollection services)
    {
        services.AddSingleton<ICardTagService, CardTagService>();
        services.AddSingleton<ICardDeckService, CardDeckService>();
        services.AddSingleton<ICardService, CardService>();
        services.AddSingleton<ICardImportExportService, CardImportExportService>();
        services.AddSingleton<INoteToCardsService, NoteToCardsService>();

        // Стратегия повторений — keyed-сервис с резолвером, как IGradeForecastStrategy в Органайзере
        // (Phase 7/11): сегодня реализация одна, но Card.SchedulerName и резолвер уже на месте.
        services.AddKeyedSingleton<IReviewScheduler, Sm2ReviewScheduler>(Sm2ReviewScheduler.Key);
        services.AddSingleton<IReviewSchedulerResolver, ReviewSchedulerResolver>();

        services.AddSingleton<IReviewQueueService, ReviewQueueService>();
        services.AddSingleton<IStudySessionService, StudySessionService>();
        services.AddSingleton<ICramPlanService, CramPlanService>();
        services.AddSingleton<ICardStatisticsService, CardStatisticsService>();

        // Один экземпляр в двух ролях — тот же приём, что у планировщика напоминаний Органайзера.
        services.AddSingleton<CardsReminderHostedService>();
        services.AddSingleton<ICardReminderScheduler>(sp => sp.GetRequiredService<CardsReminderHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<CardsReminderHostedService>());

        services.AddHostedService<CardTrashRetentionHostedService>();

        return services;
    }
}
