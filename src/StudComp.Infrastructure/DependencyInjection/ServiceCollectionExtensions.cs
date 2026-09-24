using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Infrastructure.Backup;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;
using StudComp.Infrastructure.Workspace;

namespace StudComp.Infrastructure.DependencyInjection;

/// <summary>
/// Точка входа инфраструктурного слоя в DI-контейнер (ARCHITECTURE §6): один <c>AddXxx</c>-метод.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует платформо-нейтральные сервисы инфраструктуры и привязывает Options-секции.
    /// </summary>
    /// <remarks>
    /// WPF-зависимые реализации (<c>ITrayService</c>, <c>IToastService</c>, <c>IAutostartService</c>)
    /// регистрируются отдельно в <c>StudComp.App</c> — здесь только то, что не тянет UI (ADR §16.16).
    /// </remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFileSystem, SystemFileSystem>();

        // Хэш содержимого файла: один и тот же у Архивариуса и у импорта в учебную папку — иначе
        // разложенный файл не опознавался бы дублем при повторном перетаскивании (ADR §16.30).
        services.AddSingleton<IFileHasher, FileHasher>();

        // Stateless, читает/пишет один файл под собственным замком — singleton безопасен.
        services.AddSingleton<UserSettingsProvider>();

        // Учебная папка (new_addons.md §1.1) — поверх IFileSystem + IOptionsMonitor<WorkspaceOptions>.
        services.AddSingleton<IStudyWorkspace, StudyWorkspace>();

        // Импорт файлов из проводника в папку предмета (new_addons.md §1.11). Только файловая
        // работа: учёт FileRecord и ленту активности ведёт вызывающий — Infrastructure не ссылается
        // на Data (ARCHITECTURE §5.1).
        services.AddSingleton<IWorkspaceImportService, WorkspaceImportService>();

        // Шина сообщений между модулями (ARCHITECTURE §11.4): единственная санкционированная замена
        // прямым ссылкам Modules.* → Modules.*. Свой экземпляр, а не статический
        // WeakReferenceMessenger.Default — иначе параллельные тесты делили бы одну шину (ADR §16.52).
        services.TryAddSingleton<IMessenger>(_ => new WeakReferenceMessenger());

        // Резервное копирование БД + настроек (new_addons.md §7 §9). Зависит только от Core-порта
        // IDatabaseBackupPort (реализация — в StudComp.Data), поэтому правило «Infrastructure не
        // ссылается на Data» соблюдено.
        services.AddSingleton<IBackupService, BackupService>();

        services.Configure<AppearanceOptions>(configuration.GetSection(AppearanceOptions.SectionName));
        services.Configure<GeneralOptions>(configuration.GetSection(GeneralOptions.SectionName));
        services.Configure<WorkspaceOptions>(configuration.GetSection(WorkspaceOptions.SectionName));
        services.Configure<ArchivistOptions>(configuration.GetSection(ArchivistOptions.SectionName));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<ReportForgeOptions>(configuration.GetSection(ReportForgeOptions.SectionName));
        services.Configure<UserProfileSettings>(configuration.GetSection(UserProfileSettings.SectionName));
        services.Configure<DiagnosticsOptions>(configuration.GetSection(DiagnosticsOptions.SectionName));
        services.Configure<DataOptions>(configuration.GetSection(DataOptions.SectionName));
        services.Configure<CardsOptions>(configuration.GetSection(CardsOptions.SectionName));
        services.Configure<UpdateOptions>(configuration.GetSection(UpdateOptions.SectionName));

        // Защита от дублирования List<T>-опций при биндинге секции конфигурации (new_addons.md
        // §10.2): ConfigurationBinder дописывает элементы в список, если свойство уже не пусто на
        // момент Bind() (а дефолт в этих двух Options-классах — намеренно непустой, его используют
        // тесты напрямую вне DI). PostConfigure идёт последним шагом после любого Configure<T> и
        // чинит это идемпотентно — в том числе для пользователей, уже получивших задублированный
        // список в usersettings.json на 1.0.0.
        services.PostConfigure<WorkspaceOptions>(o =>
            o.SubjectFolderTemplate = DeduplicateKeepOrder(o.SubjectFolderTemplate));
        services.PostConfigure<ArchivistOptions>(o =>
        {
            o.IgnoredPatterns = DeduplicateKeepOrder(o.IgnoredPatterns);

            // Список наблюдаемых папок дефолтом пуст, поэтому дублирования при биндинге не ловит, но
            // одну и ту же папку легко добавить дважды руками — дедуп симметрии ради (Phase 13.2).
            o.WatchedFolders = DeduplicateKeepOrder(o.WatchedFolders);
        });

        return services;
    }

    private static List<string> DeduplicateKeepOrder(List<string> items) =>
        items.Count == 0 ? items : items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
