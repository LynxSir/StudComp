using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StudComp.Controls;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Startup;
using StudComp.Services;
using StudComp.ViewModels.Archivist;
using StudComp.ViewModels.Cards;
using StudComp.ViewModels.Organizer;
using StudComp.ViewModels.Organizer.Hub;
using StudComp.ViewModels.ReportForge;
using StudComp.ViewModels.Settings;
using StudComp.ViewModels.Shell;
using StudComp.Views.Cards;

namespace StudComp.DependencyInjection;

/// <summary>
/// Регистрация сервисов уровня хоста/UI (ARCHITECTURE §6): навигация, WPF-зависимые реализации
/// инфраструктурных интерфейсов, окна и ViewModel'и.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Задел из эскиза §6. В <c>Core</c> — только абстракции и статические хелперы, регистрировать
    /// нечего; метод существует, чтобы composition root читался единообразно и было куда добавлять
    /// регистрации, если они появятся.
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services) => services;

    /// <summary>
    /// Сервисы, живущие в <c>StudComp.App</c>, потому что зависят от WPF (ADR §16.16):
    /// трей, тосты-заглушка, автозапуск, тема, навигация — плюс окна и страницы-ViewModel'и.
    /// </summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IMotionService, MotionService>();
        services.AddSingleton<ITrayService, TrayService>();
        // Настоящие Windows-тосты (Phase 13, ADR §16.20), когда окно скрыто; ToastService —
        // канал для карточек ToastHost при видимом окне и тихий откат на балун трея.
        services.AddSingleton<ToastService>();
        services.AddSingleton<IToastService, WindowsToastService>();
        services.AddSingleton<IAutostartService, RegistryAutostartService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IShellLauncher, ShellLauncher>();

        // Активный предмет (new_addons.md §1.8): singleton + IHostedService ради момента создания.
        services.AddSingleton<ActiveSubjectProvider>();
        services.AddSingleton<IActiveSubjectProvider>(sp => sp.GetRequiredService<ActiveSubjectProvider>());
        services.AddHostedService(sp => sp.GetRequiredService<ActiveSubjectProvider>());

        // Агрегатор данных Дашборда (new_addons.md §1.5).
        services.AddSingleton<IDashboardService, DashboardService>();

        // Запись в ленту активности разложенных файлов + ретеншн-прунинг.
        services.AddHostedService<ActivityLogListener>();

        // Диагностика §14: по умолчанию бездействует, включается флагом Rubrica:Diagnostics.
        services.AddHostedService<PerformanceProbe>();
        services.AddHostedService<UiResponsivenessProbe>();

        // Разовая проверка обновлений при старте (Phase 13, §11.6): единственный сетевой вызов,
        // с opt-out, с задержкой — холодный старт не трогаем.
        services.AddHostedService<StartupUpdateCheck>();

        // Модальные диалоги Shell'а: хост подключается в MainWindow, обёртка — IDialogService.
        services.AddSingleton<Wpf.Ui.IContentDialogService, Wpf.Ui.ContentDialogService>();
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ToastHostViewModel>();
        services.AddSingleton<SettingsShellViewModel>();

        // Разделы модалки настроек (new_addons.md §7). Порядок регистрации = порядок рельса;
        // SettingsShellViewModel всё равно сортирует по enum. Singleton: shell живёт весь процесс.
        services.AddSingleton<ISettingsSectionViewModel, GeneralSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, AppearanceSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, WorkspaceSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, ArchivistSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, SemesterSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, NotificationSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, ReportSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, AutostartSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, DataSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, CardsSettingsViewModel>();
        services.AddSingleton<ISettingsSectionViewModel, AboutViewModel>();

        // Страницы-ViewModel'и — Transient (ARCHITECTURE §6): каждая навигация даёт свежий экземпляр.
        services.AddTransient<DashboardPageViewModel>();
        services.AddTransient<ArchivistPageViewModel>();
        services.AddTransient<OrganizerPageViewModel>();
        services.AddTransient<ReportForgePageViewModel>();
        services.AddTransient<CardsPageViewModel>();

        // Вкладки Картотеки — живут внутри CardsPageViewModel (new_addons.md §2.2).
        services.AddTransient<CardLibraryViewModel>();
        services.AddTransient<CardReviewViewModel>();
        services.AddTransient<CardExamViewModel>();
        services.AddTransient<CardStatsViewModel>();

        // Экран сессии — отдельная полноэкранная страница навигации (new_addons.md §2.2, §8.3).
        services.AddTransient<StudySessionViewModel>();

        // Оверлей Ctrl+K живёт столько же, сколько окно: он общий для всех экранов.
        services.AddSingleton<CardPaletteViewModel>();

        // Компактный режим-шпаргалка (new_addons.md §8.6) — singleton-окно: Hide()/Show() между
        // вызовами, состояние поиска не теряется. Настоящее закрытие — только через ForceClose()
        // при выходе из приложения.
        services.AddSingleton<CardCheatSheetViewModel>();
        services.AddSingleton<CardCheatSheetWindow>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        // Панель просмотра и правки карточки (Controls/CardPreview) — свой экземпляр на страницу.
        services.AddTransient<CardPreviewViewModel>();

        // Вкладки Архивариуса — живут внутри ArchivistPageViewModel.
        services.AddTransient<UnsortedFilesViewModel>();
        services.AddTransient<RulesViewModel>();
        services.AddTransient<OperationsLogViewModel>();

        // Панель предпросмотра файла (Controls/FilePreview, Phase 12.2) — свой экземпляр на каждую
        // страницу, которая её использует.
        services.AddTransient<FilePreviewViewModel>();

        // Хаб предмета (new_addons.md §5) и его вкладки — страница вне сайдбара, навигация с параметром.
        services.AddTransient<SubjectHubViewModel>();
        services.AddTransient<DeadlineWorkViewModel>();
        services.AddTransient<HubOverviewViewModel>();
        services.AddTransient<HubFilesViewModel>();
        services.AddTransient<HubScheduleViewModel>();
        services.AddTransient<HubNotesViewModel>();
        services.AddTransient<HubCardsViewModel>();
        services.AddTransient<HubDeadlinesViewModel>();
        services.AddTransient<NoteEditorViewModel>();

        // Вкладки Органайзера — тоже Transient, живут внутри OrganizerPageViewModel.
        services.AddTransient<ScheduleViewModel>();
        services.AddTransient<DeadlinesViewModel>();
        services.AddTransient<SubjectsViewModel>();
        services.AddTransient<GradeBookViewModel>();

        // Вкладки раздела «Отчёты» — живут внутри ReportForgePageViewModel.
        services.AddTransient<NewReportViewModel>();
        services.AddTransient<ReportHistoryViewModel>();
        services.AddTransient<ProfilesViewModel>();

        return services;
    }
}
