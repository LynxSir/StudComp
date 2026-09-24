using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using StudComp.Data;
using StudComp.Data.DependencyInjection;
using StudComp.DependencyInjection;
using StudComp.Infrastructure.DependencyInjection;
using StudComp.Infrastructure.Logging;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.DependencyInjection;
using StudComp.Modules.Cards.DependencyInjection;
using StudComp.Modules.Organizer.DependencyInjection;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.ReportForge.DependencyInjection;
using StudComp.Resources;
using StudComp.Services;
using StudComp.Views.Cards;
using StudComp.Views.Shell;

namespace StudComp;

/// <summary>
/// Composition root приложения (ARCHITECTURE §6): поднимает generic host, накатывает миграции под
/// сплэшем, применяет тему, создаёт иконку в трее и показывает главное окно.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    public App()
    {
        // Bootstrap-логгер до сборки хоста — чтобы ранние сбои попали хоть куда-то.
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Debug()
            .CreateBootstrapLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Замер холодного старта (NFR §14, цель < 1.5 с): от входа в OnStartup до первой отрисовки
        // MainWindow. Спан миграций выделяется отдельно — первый запуск с накатом схемы честно
        // отличается от прогретого.
        var startupClock = Stopwatch.StartNew();
        var migrateMs = 0L;

        var splash = new SplashWindow();
        splash.Show();

        try
        {
            _host = BuildHost();
            await _host.StartAsync();

            // Каталог данных + все невыполненные миграции (ARCHITECTURE §7.3).
            var migrateClock = Stopwatch.StartNew();
            await _host.Services.GetRequiredService<IDbInitializer>().InitializeAsync();
            migrateMs = migrateClock.ElapsedMilliseconds;

            var services = _host.Services;

            // Однократные миграции пользовательских настроек между версиями (new_addons.md §1.1).
            var settingsProvider = services.GetRequiredService<UserSettingsProvider>();
            var settingsLogger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SettingsMigrations");
            SettingsMigrations.Run(settingsProvider, settingsLogger);

            // Перенос старой пары настроек «дата начала семестра + чётность» в сущность Semester
            // (new_addons.md §5). Сид идемпотентен, живёт в Органайзере, а порядок вызова — здесь:
            // сущность появляется после миграций схемы и до первого обращения UI к семестру.
            await SeedSemesterAsync(services, settingsProvider, settingsLogger);

            // Тема, затем акцент/плотность/масштаб/анимации (new_addons.md §7 §2) — СТРОГО ДО создания
            // главного окна (Phase 13.10): и подмена словарей темы WPF-UI, и записи в ресурсы
            // приложения на уже построенном дереве стоят по полному обходу окна каждая, а на живом
            // дереве именно на них падал слой композиции WPF. На пустом дереве это дёшево и безопасно.
            var theme = services.GetRequiredService<IThemeService>();
            theme.Apply(services.GetRequiredService<IOptions<AppearanceOptions>>().Value.Theme);
            services.GetRequiredService<IMotionService>().Initialize();

            var mainWindow = services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            theme.Initialize(mainWindow);

            mainWindow.ContentRendered += LogColdStart;

            void LogColdStart(object? sender, EventArgs args)
            {
                mainWindow.ContentRendered -= LogColdStart;
                Log.Information(
                    "Холодный старт: {TotalMs} мс (миграции: {MigrateMs} мс)",
                    startupClock.ElapsedMilliseconds, migrateMs);
            }

            // Компактный режим-шпаргалка (new_addons.md §8.6) — окно создаётся сразу (хендл нужен
            // глобальной горячей клавише), но не показывается: вызов из раздела «Картотека» или трея.
            var cheatSheetWindow = services.GetRequiredService<CardCheatSheetWindow>();
            services.GetRequiredService<IGlobalHotkeyService>()
                .Initialize(cheatSheetWindow, cheatSheetWindow.ToggleVisibility);

            var tray = services.GetRequiredService<ITrayService>();
            tray.ExitRequested += (_, _) =>
            {
                cheatSheetWindow.ForceClose();
                Shutdown();
            };
            tray.CheatSheetRequested += (_, _) => cheatSheetWindow.ShowAndFocus();
            tray.Initialize();

            // Свёрнутый запуск (new_addons.md §7 §8): окно создаётся, но не показывается — доступ
            // через иконку в трее. MinimizeToTrayOnClose при этом обычно тоже включён.
            if (!services.GetRequiredService<IOptions<GeneralOptions>>().Value.LaunchMinimized)
            {
                mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Не удалось запустить приложение");
            MessageBox.Show(
                AppText.StartupFailed,
                AppText.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
        finally
        {
            splash.Close();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    /// <summary>
    /// Разовый перенос устаревшей секции <c>Rubrica:Organizer</c> в сущность <c>Semester</c>
    /// (new_addons.md §5). Сбой сида не должен мешать запуску: без семестра приложение работает,
    /// просто не фильтрует расписание по чётности.
    /// </summary>
    private static async Task SeedSemesterAsync(
        IServiceProvider services, UserSettingsProvider settings, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            var legacy = SettingsMigrations.ReadLegacySemester(settings);
            var semesters = services.GetRequiredService<ISemesterService>();

            var result = await semesters.SeedFromLegacyAsync(legacy.SemesterStartDate, legacy.FirstWeekIsOdd);
            if (result.IsFailure)
            {
                logger.LogWarning("Не удалось перенести настройки семестра: {Error}", result.Error.Message);
                return;
            }

            SettingsMigrations.ClearLegacySemester(settings, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Сид семестра из старых настроек не выполнен");
        }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateDefaultBuilder();

        // Контент-рут — рядом с exe, а не текущий каталог: иначе appsettings.json не найдётся при
        // запуске из ярлыка/трея.
        builder.UseContentRoot(AppContext.BaseDirectory);

#if DEBUG
        builder.UseEnvironment(Environments.Development);
#endif

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // appsettings.json / appsettings.{Env}.json уже добавлены CreateDefaultBuilder.
            // Сверху — пользовательский оверлей (ARCHITECTURE §11.2).
            config.AddJsonFile(UserSettingsPaths.UserSettingsFile, optional: true, reloadOnChange: true);
        });

        builder.UseSerilog((context, loggerConfiguration) =>
            SerilogSetup.Configure(loggerConfiguration, context.Configuration));

        builder.ConfigureServices((context, services) =>
        {
            services.AddCoreServices();
            services.AddDataLayer(context.Configuration);
            services.AddInfrastructure(context.Configuration);
            services.AddArchivistModule();
            services.AddCardsModule();
            services.AddOrganizerModule();
            services.AddReportForgeModule();
            services.AddAppServices();
        });

        return builder.Build();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Необработанное исключение в UI-потоке");
        MessageBox.Show(
            AppText.UnhandledUiError,
            AppText.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Error(e.ExceptionObject as Exception, "Необработанное исключение в домене приложения");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Необработанное исключение в фоновой задаче");
        e.SetObserved();
    }
}
