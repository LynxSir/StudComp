using Microsoft.Extensions.DependencyInjection;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Modules.ReportForge.Services;

namespace StudComp.Modules.ReportForge.DependencyInjection;

/// <summary>
/// Точка входа модуля «Комбайн отчётов» в DI-контейнер (ARCHITECTURE §6, §17).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует парсер Markdown, рендерер DOCX, поставщик профилей и пайплайн. Репозитории
    /// приходят из <c>AddDataLayer</c>, файловая система и настройки — из <c>AddInfrastructure</c>.
    /// </summary>
    public static IServiceCollection AddReportForgeModule(this IServiceCollection services)
    {
        // Все сервисы модуля без состояния между вызовами — singleton (ARCHITECTURE §6).
        services.AddSingleton<IMarkdownDocumentModelBuilder, MarkdownDocumentModelBuilder>();
        services.AddSingleton<IGostDocxRenderer, GostDocxRenderer>();
        services.AddSingleton<IGostStyleProfileProvider, GostStyleProfileProvider>();
        services.AddSingleton<IReportTemplateService, ReportTemplateService>();
        services.AddSingleton<IReportPipeline, ReportPipeline>();

        return services;
    }
}
