using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Infrastructure.Settings;

namespace StudComp.Infrastructure.Tests;

/// <summary>
/// Миграция настроек «корень архива → учебная папка» (new_addons.md §1.1) должна быть идемпотентной и
/// не трогать соседние секции.
/// </summary>
public sealed class SettingsMigrationsTests : IDisposable
{
    private readonly string _file =
        Path.Combine(Path.GetTempPath(), "rubrica-migr-" + Guid.NewGuid().ToString("N"), "usersettings.json");

    private UserSettingsProvider Provider() => new(NullLogger<UserSettingsProvider>.Instance, _file);

    private void RunMigration() => SettingsMigrations.Run(Provider(), NullLogger.Instance);

    [Fact]
    public void Copies_archive_root_into_empty_study_root()
    {
        Provider().Update<ArchivistOptions>(ArchivistOptions.SectionName, o => o.ArchiveRootFolder = @"D:\Учёба");

        RunMigration();

        Assert.Equal(@"D:\Учёба", Provider().Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath);
    }

    [Fact]
    public void Leaves_study_root_untouched_when_already_set()
    {
        Provider().Update<ArchivistOptions>(ArchivistOptions.SectionName, o => o.ArchiveRootFolder = @"D:\Архив");
        Provider().Update<WorkspaceOptions>(WorkspaceOptions.SectionName, o => o.StudyRootPath = @"E:\Уже\Задано");

        RunMigration();

        Assert.Equal(@"E:\Уже\Задано", Provider().Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath);
    }

    [Fact]
    public void Is_noop_when_archive_root_is_empty()
    {
        RunMigration();

        Assert.Equal(string.Empty, Provider().Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath);
    }

    [Fact]
    public void Is_idempotent_on_a_second_run()
    {
        Provider().Update<ArchivistOptions>(ArchivistOptions.SectionName, o => o.ArchiveRootFolder = @"D:\Учёба");

        RunMigration();
        // Пользователь потом сменил учебную папку — повторный прогон не должен её вернуть на корень архива.
        Provider().Update<WorkspaceOptions>(WorkspaceOptions.SectionName, o => o.StudyRootPath = @"F:\Новая");
        RunMigration();

        Assert.Equal(@"F:\Новая", Provider().Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath);
    }

    [Fact]
    public void Preserves_sibling_general_settings()
    {
        Provider().Update<GeneralOptions>(GeneralOptions.SectionName, o => o.RunOnWindowsStartup = true);
        Provider().Update<ArchivistOptions>(ArchivistOptions.SectionName, o => o.ArchiveRootFolder = @"D:\Учёба");

        RunMigration();

        Assert.True(Provider().Get<GeneralOptions>(GeneralOptions.SectionName).RunOnWindowsStartup);
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_file)!;
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
