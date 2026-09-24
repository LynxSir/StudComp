using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Data;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Infrastructure.Backup;
using StudComp.Infrastructure.DependencyInjection;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;
using StudComp.Infrastructure.Workspace;

namespace StudComp.Infrastructure.Tests;

/// <summary>
/// <c>AddInfrastructure</c> должен привязывать Options-секции из конфигурации и регистрировать
/// платформо-нейтральные сервисы.
/// </summary>
public sealed class InfrastructureRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration);

        // Порт БД живёт в StudComp.Data; здесь подменяем фейком, чтобы IBackupService резолвился.
        services.AddSingleton<IDatabaseBackupPort, NoopBackupPort>();

        return services.BuildServiceProvider();
    }

    private sealed class NoopBackupPort : IDatabaseBackupPort
    {
        public Task WriteSnapshotAsync(string destinationFilePath, CancellationToken ct = default) => Task.CompletedTask;

        public Task ReplaceDatabaseAsync(string sourceFilePath, string? destinationFilePath = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void Binds_appearance_section_from_configuration()
    {
        using var provider = Build(new() { ["Rubrica:Appearance:Theme"] = "Dark" });

        var options = provider.GetRequiredService<IOptions<AppearanceOptions>>().Value;

        Assert.Equal(AppTheme.Dark, options.Theme);
    }

    [Fact]
    public void Applies_defaults_when_section_absent()
    {
        using var provider = Build(new());

        var general = provider.GetRequiredService<IOptions<GeneralOptions>>().Value;
        var notifications = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;

        Assert.True(general.MinimizeToTrayOnClose);
        Assert.Equal(30, notifications.RemindMinutesBefore);
    }

    [Fact]
    public void Registers_file_system_and_settings_provider()
    {
        using var provider = Build(new());

        Assert.IsType<SystemFileSystem>(provider.GetRequiredService<IFileSystem>());
        Assert.NotNull(provider.GetRequiredService<UserSettingsProvider>());
    }

    [Fact]
    public void Binds_workspace_section_and_registers_study_workspace()
    {
        using var provider = Build(new()
        {
            ["Rubrica:Workspace:StudyRootPath"] = @"D:\Учёба",
            ["Rubrica:Workspace:SubjectFolderTemplate:0"] = "Лекции",
        });

        var options = provider.GetRequiredService<IOptions<WorkspaceOptions>>().Value;
        Assert.Equal(@"D:\Учёба", options.StudyRootPath);
        Assert.Contains("Лекции", options.SubjectFolderTemplate);
        Assert.IsType<StudyWorkspace>(provider.GetRequiredService<IStudyWorkspace>());
    }

    [Fact]
    public void Light_is_the_default_theme()
    {
        using var provider = Build(new());

        Assert.Equal(AppTheme.Light, provider.GetRequiredService<IOptions<AppearanceOptions>>().Value.Theme);
    }

    [Fact]
    public void Binds_data_section_and_applies_defaults()
    {
        using var configured = Build(new() { ["Rubrica:Data:ActivityRetentionDays"] = "30" });
        Assert.Equal(30, configured.GetRequiredService<IOptions<DataOptions>>().Value.ActivityRetentionDays);

        using var defaults = Build(new());
        var data = defaults.GetRequiredService<IOptions<DataOptions>>().Value;
        Assert.Equal(90, data.ActivityRetentionDays);
        Assert.Equal(500, data.ActivityRetentionKeepCount);
        Assert.Equal(180, data.OperationLogRetentionDays);
    }

    [Fact]
    public void Registers_backup_service()
    {
        using var provider = Build(new());

        Assert.IsType<BackupService>(provider.GetRequiredService<IBackupService>());
    }

    [Fact]
    public void Appearance_defaults_include_new_fields()
    {
        using var provider = Build(new());

        var appearance = provider.GetRequiredService<IOptions<AppearanceOptions>>().Value;
        Assert.Equal("wine", appearance.Accent);
        Assert.Equal(AppDensity.Normal, appearance.Density);
        Assert.Equal(1.0, appearance.FontScale);
        Assert.True(appearance.AnimationsEnabled);
    }

    /// <summary>
    /// new_addons.md §10.2 (Тест.txt №27): ConfigurationBinder дописывает элементы List&lt;T&gt;, если
    /// свойство уже несёт непустой C#-дефолт — appsettings.json повторяет тот же дефолт явно, поэтому
    /// единственный Configure&lt;T&gt; давал список вдвое длиннее. Секция здесь заполнена тем же
    /// набором, что appsettings.json, — это и воспроизводило дублирование; PostConfigure должен его
    /// убрать.
    /// </summary>
    [Fact]
    public void Workspace_subject_folder_template_is_not_duplicated_by_binding()
    {
        using var provider = Build(new()
        {
            ["Rubrica:Workspace:SubjectFolderTemplate:0"] = "Лекции",
            ["Rubrica:Workspace:SubjectFolderTemplate:1"] = "Практики",
            ["Rubrica:Workspace:SubjectFolderTemplate:2"] = "Проекты",
            ["Rubrica:Workspace:SubjectFolderTemplate:3"] = "Отчёты",
            ["Rubrica:Workspace:SubjectFolderTemplate:4"] = "Исходники",
        });

        var template = provider.GetRequiredService<IOptions<WorkspaceOptions>>().Value.SubjectFolderTemplate;
        Assert.Equal(5, template.Count);
        Assert.Equal(["Лекции", "Практики", "Проекты", "Отчёты", "Исходники"], template);
    }

    /// <summary>Тот же баг и тот же фикс — для второго затронутого списка (new_addons.md §10.2).</summary>
    [Fact]
    public void Archivist_ignored_patterns_is_not_duplicated_by_binding()
    {
        using var provider = Build(new()
        {
            ["Rubrica:Archivist:IgnoredPatterns:0"] = "*.tmp",
            ["Rubrica:Archivist:IgnoredPatterns:1"] = "*.crdownload",
            ["Rubrica:Archivist:IgnoredPatterns:2"] = "*.part",
            ["Rubrica:Archivist:IgnoredPatterns:3"] = "*.partial",
            ["Rubrica:Archivist:IgnoredPatterns:4"] = "*.download",
            ["Rubrica:Archivist:IgnoredPatterns:5"] = "~$*",
            ["Rubrica:Archivist:IgnoredPatterns:6"] = "*.lnk",
            ["Rubrica:Archivist:IgnoredPatterns:7"] = "desktop.ini",
            ["Rubrica:Archivist:IgnoredPatterns:8"] = "thumbs.db",
        });

        var patterns = provider.GetRequiredService<IOptions<ArchivistOptions>>().Value.IgnoredPatterns;
        Assert.Equal(9, patterns.Count);
    }

    /// <summary>
    /// Наблюдение за учебной папкой включено по умолчанию (Phase 13.2): указав её один раз, человек
    /// не должен дублировать тот же путь в списке наблюдаемых папок.
    /// </summary>
    [Fact]
    public void Archivist_watches_the_study_root_by_default()
    {
        using var provider = Build([]);

        var options = provider.GetRequiredService<IOptions<ArchivistOptions>>().Value;

        Assert.True(options.WatchStudyRoot);
        Assert.Empty(options.WatchedFolders);
    }

    /// <summary>Одну и ту же папку легко добавить дважды руками — дедуп идёт тем же PostConfigure.</summary>
    [Fact]
    public void Archivist_watched_folders_are_deduplicated()
    {
        using var provider = Build(new()
        {
            ["Rubrica:Archivist:WatchedFolders:0"] = @"C:\Загрузки",
            ["Rubrica:Archivist:WatchedFolders:1"] = @"c:\загрузки",
            ["Rubrica:Archivist:WatchedFolders:2"] = @"C:\Рабочий стол",
        });

        var folders = provider.GetRequiredService<IOptions<ArchivistOptions>>().Value.WatchedFolders;

        Assert.Equal(2, folders.Count);
        Assert.Equal([@"C:\Загрузки", @"C:\Рабочий стол"], folders);
    }
}
