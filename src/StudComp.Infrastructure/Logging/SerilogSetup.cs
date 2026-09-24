using Microsoft.Extensions.Configuration;
using Serilog;
using StudComp.Core.Common;

namespace StudComp.Infrastructure.Logging;

/// <summary>
/// Общая настройка Serilog (ARCHITECTURE §11.1): файловый sink с посуточной ротацией и хранением
/// 14 дней в <c>%LocalAppData%\Rubrica\logs\</c>, плюс вывод в отладчик. Уровень по умолчанию —
/// <c>Information</c>, переопределяется секцией <c>Serilog</c> в
/// <c>appsettings*.json</c> (напр. <c>Debug</c> в Development).
/// </summary>
public static class SerilogSetup
{
    /// <summary>
    /// Применяет конфигурацию логгера. Вызывается из <c>App.xaml.cs</c> через
    /// <c>UseSerilog((ctx, cfg) =&gt; SerilogSetup.Configure(cfg, ctx.Configuration))</c>.
    /// </summary>
    /// <param name="logger">Конфигурируемый логгер.</param>
    /// <param name="configuration">Конфигурация приложения (секция <c>Serilog</c> может менять уровень).</param>
    /// <param name="logDirectory">Каталог лог-файлов; по умолчанию <see cref="RubricaPaths.LogsDirectory"/> (параметр — для тестов).</param>
    public static void Configure(LoggerConfiguration logger, IConfiguration configuration, string? logDirectory = null)
    {
        logDirectory ??= RubricaPaths.LogsDirectory;
        Directory.CreateDirectory(logDirectory);

        logger
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            // Значения из appsettings имеют приоритет над кодом выше — можно поднять/опустить уровень
            // без пересборки. Отсутствие секции не ошибка.
            .ReadFrom.Configuration(configuration)
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
    }
}
