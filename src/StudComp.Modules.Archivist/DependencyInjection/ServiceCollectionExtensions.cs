using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.DependencyInjection;

/// <summary>
/// Точка входа модуля «Архивариус» в DI-контейнер (ARCHITECTURE §6, §17): один <c>AddXxxModule</c>-метод.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует движок правил, исполнитель файловых операций и фоновый наблюдатель за папкой.
    /// Репозитории приходят из <c>AddDataLayer</c>, настройки и файловая система - из <c>AddInfrastructure</c>.
    /// </summary>
    public static IServiceCollection AddArchivistModule(this IServiceCollection services)
    {
        // Единственное место, где считается целевая папка предмета (new_addons.md §4): движок правил
        // и ручная сортировка обязаны давать один и тот же путь.
        services.AddSingleton<SubjectTargetResolver>();

        services.AddSingleton<IFileStabilityChecker, FileStabilityChecker>();
        services.AddSingleton<ISortingRuleEngine, SortingRuleEngine>();
        services.AddSingleton<IFileOperationExecutor, FileOperationExecutor>();
        services.AddSingleton<IArchivistRuleService, ArchivistRuleService>();
        services.AddSingleton<IUnsortedFileService, UnsortedFileService>();
        services.AddSingleton<IRulePreviewService, RulePreviewService>();
        services.AddSingleton<IOperationHistoryService, OperationHistoryService>();
        services.AddSingleton<IUnsortedSuggestionService, UnsortedSuggestionService>();

        // Развязывает наблюдателя и сверщика: они нужны друг другу, но ссылаться напрямую не могут —
        // получился бы цикл на двух singleton-ах (ADR §16.53).
        services.AddSingleton<ReconciliationSignal>();

        // Один экземпляр обслуживает и роль IFileWatcherService (состояние и RequestRescan для UI),
        // и роль IHostedService (жизненный цикл хоста).
        services.AddSingleton<FileWatcherHostedService>();
        services.AddSingleton<IFileWatcherService>(sp => sp.GetRequiredService<FileWatcherHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<FileWatcherHostedService>());

        // Тот же тройной шаблон: сверка живёт как hosted service, а UI дёргает её через интерфейс.
        services.AddSingleton<ReconciliationHostedService>();
        services.AddSingleton<IReconciliationService>(sp => sp.GetRequiredService<ReconciliationHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<ReconciliationHostedService>());

        return services;
    }
}
