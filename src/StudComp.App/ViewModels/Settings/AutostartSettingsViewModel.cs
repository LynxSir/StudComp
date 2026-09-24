using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;
using StudComp.Infrastructure.Startup;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>Раздел «Автозапуск» (new_addons.md §7 §8): старт с Windows, свёрнутый запуск.</summary>
public sealed partial class AutostartSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly UserSettingsProvider _settings;
    private readonly IAutostartService _autostart;

    public AutostartSettingsViewModel(
        UserSettingsProvider settings,
        IAutostartService autostart,
        IOptions<GeneralOptions> general)
    {
        _settings = settings;
        _autostart = autostart;

        using (BeginLoad())
        {
            // Источник истины для автозапуска — реальное состояние реестра, а не файл настроек.
            _runOnWindowsStartup = autostart.IsEnabled;
            _launchMinimized = general.Value.LaunchMinimized;
        }
    }

    public override SettingsSection Section => SettingsSection.Autostart;

    public override string Title => "Автозапуск";

    public override SymbolRegular Icon => SymbolRegular.Power24;

    public override IEnumerable<string> SearchKeywords =>
        ["автозапуск", "запуск с windows", "старт", "реестр", "свёрнутый запуск", "в трей"];

    [ObservableProperty]
    private bool _runOnWindowsStartup;

    [ObservableProperty]
    private bool _launchMinimized;

    partial void OnRunOnWindowsStartupChanged(bool value)
    {
        if (IsLoading)
        {
            return;
        }

        if (value)
        {
            _autostart.Enable();
        }
        else
        {
            _autostart.Disable();
        }

        _settings.Update<GeneralOptions>(GeneralOptions.SectionName, o => o.RunOnWindowsStartup = value);
    }

    partial void OnLaunchMinimizedChanged(bool value)
    {
        if (!IsLoading)
        {
            _settings.Update<GeneralOptions>(GeneralOptions.SectionName, o => o.LaunchMinimized = value);
        }
    }
}
