using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Infrastructure.Settings;

namespace StudComp.Infrastructure.Tests;

/// <summary>
/// Провайдер пользовательских настроек должен переживать перезапись отдельной секции, не теряя
/// остальное содержимое файла, и корректно читать сохранённое обратно.
/// </summary>
public sealed class UserSettingsProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "rubrica-tests-" + Guid.NewGuid().ToString("N"));

    private UserSettingsProvider CreateProvider() =>
        new(NullLogger<UserSettingsProvider>.Instance, Path.Combine(_tempDir, "usersettings.json"));

    [Fact]
    public void Update_then_Get_returns_written_value()
    {
        var provider = CreateProvider();

        provider.Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.Theme = AppTheme.Dark);

        // Свежий экземпляр — читаем именно из файла, а не из памяти.
        var reloaded = CreateProvider().Get<AppearanceOptions>(AppearanceOptions.SectionName);
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
    }

    [Fact]
    public void Update_preserves_unrelated_sections()
    {
        var provider = CreateProvider();

        provider.Update<GeneralOptions>(GeneralOptions.SectionName, o => o.RunOnWindowsStartup = true);
        provider.Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.Theme = AppTheme.Light);

        var general = provider.Get<GeneralOptions>(GeneralOptions.SectionName);
        Assert.True(general.RunOnWindowsStartup);
        Assert.Equal(AppTheme.Light, provider.Get<AppearanceOptions>(AppearanceOptions.SectionName).Theme);
    }

    [Fact]
    public void Get_on_missing_file_returns_defaults()
    {
        var options = CreateProvider().Get<GeneralOptions>(GeneralOptions.SectionName);

        Assert.False(options.RunOnWindowsStartup);
        Assert.True(options.MinimizeToTrayOnClose);
    }

    [Fact]
    public void Written_file_is_valid_nested_json()
    {
        CreateProvider().Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.Theme = AppTheme.Dark);

        var json = File.ReadAllText(Path.Combine(_tempDir, "usersettings.json"));
        Assert.Contains("\"Rubrica\"", json);
        Assert.Contains("\"Appearance\"", json);
        Assert.Contains("\"Dark\"", json);
    }

    [Fact]
    public void Round_trips_new_appearance_fields()
    {
        var provider = CreateProvider();

        provider.Update<AppearanceOptions>(AppearanceOptions.SectionName, o =>
        {
            o.Accent = "#123ABC";
            o.Density = AppDensity.Compact;
            o.FontScale = 1.25;
            o.AnimationsEnabled = false;
        });

        var reloaded = CreateProvider().Get<AppearanceOptions>(AppearanceOptions.SectionName);
        Assert.Equal("#123ABC", reloaded.Accent);
        Assert.Equal(AppDensity.Compact, reloaded.Density);
        Assert.Equal(1.25, reloaded.FontScale);
        Assert.False(reloaded.AnimationsEnabled);
    }

    [Fact]
    public void Round_trips_data_options()
    {
        var provider = CreateProvider();

        provider.Update<DataOptions>(DataOptions.SectionName, o =>
        {
            o.ActivityRetentionDays = 30;
            o.ActivityRetentionKeepCount = 250;
            o.OperationLogRetentionDays = 60;
        });

        var reloaded = CreateProvider().Get<DataOptions>(DataOptions.SectionName);
        Assert.Equal(30, reloaded.ActivityRetentionDays);
        Assert.Equal(250, reloaded.ActivityRetentionKeepCount);
        Assert.Equal(60, reloaded.OperationLogRetentionDays);
    }

    [Fact]
    public void Round_trips_quiet_hours_time_only()
    {
        var provider = CreateProvider();

        provider.Update<NotificationOptions>(NotificationOptions.SectionName, o =>
        {
            o.QuietHoursEnabled = true;
            o.QuietHoursStart = new TimeOnly(23, 30);
            o.QuietHoursEnd = new TimeOnly(7, 15);
        });

        var reloaded = CreateProvider().Get<NotificationOptions>(NotificationOptions.SectionName);
        Assert.True(reloaded.QuietHoursEnabled);
        Assert.Equal(new TimeOnly(23, 30), reloaded.QuietHoursStart);
        Assert.Equal(new TimeOnly(7, 15), reloaded.QuietHoursEnd);
    }

    [Fact]
    public void New_section_write_preserves_existing_sections()
    {
        var provider = CreateProvider();

        provider.Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.Theme = AppTheme.Dark);
        provider.Update<DataOptions>(DataOptions.SectionName, o => o.ActivityRetentionDays = 42);

        Assert.Equal(AppTheme.Dark, provider.Get<AppearanceOptions>(AppearanceOptions.SectionName).Theme);
        Assert.Equal(42, provider.Get<DataOptions>(DataOptions.SectionName).ActivityRetentionDays);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
