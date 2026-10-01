using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Core.Common;
using StudComp.Infrastructure.Settings;
using StudComp.Infrastructure.Startup;
using StudComp.Services;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>Раздел «О программе» (new_addons.md §7 §10): версия, обновления, лицензия, диагностика, лог.</summary>
public sealed partial class AboutViewModel : SettingsSectionViewModelBase
{
    private readonly UserSettingsProvider _settings;
    private readonly IShellLauncher _shellLauncher;
    private readonly IUpdateService _updates;
    private readonly UpdateCoordinator _updateCoordinator;

    public AboutViewModel(
        UserSettingsProvider settings,
        IShellLauncher shellLauncher,
        IUpdateService updates,
        UpdateCoordinator updateCoordinator,
        IOptions<DiagnosticsOptions> diagnostics,
        IOptions<UpdateOptions> update)
    {
        _settings = settings;
        _shellLauncher = shellLauncher;
        _updates = updates;
        _updateCoordinator = updateCoordinator;

        using (BeginLoad())
        {
            _performanceLoggingEnabled = diagnostics.Value.PerformanceLoggingEnabled;
            _autoCheckOnStartup = update.Value.AutoCheckOnStartup;
            _updateStatus = updates.IsUpdateSupported
                ? "Нажмите «Проверить», чтобы узнать о новой версии."
                : "Автообновление работает в версии, установленной через Rubrica Setup.";
        }
    }

    public override SettingsSection Section => SettingsSection.About;

    public override string Title => "О программе";

    public override SymbolRegular Icon => SymbolRegular.Info24;

    public override IEnumerable<string> SearchKeywords =>
        ["о программе", "версия", "обновления", "обновить", "лицензия", "диагностика", "производительность", "лог"];

    public string ProductName => "Rubrica";

    public string Version => _updates.CurrentVersion;

    public string LicenseText => "Все права принадлежат автору.";

    [ObservableProperty]
    private bool _performanceLoggingEnabled;

    [ObservableProperty]
    private bool _autoCheckOnStartup;

    [ObservableProperty]
    private string _updateStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isCheckingForUpdates;

    private bool CanCheckForUpdates => !IsCheckingForUpdates;

    partial void OnPerformanceLoggingEnabledChanged(bool value)
    {
        if (!IsLoading)
        {
            _settings.Update<DiagnosticsOptions>(
                DiagnosticsOptions.SectionName, o => o.PerformanceLoggingEnabled = value);
        }
    }

    partial void OnAutoCheckOnStartupChanged(bool value)
    {
        if (!IsLoading)
        {
            _settings.Update<UpdateOptions>(UpdateOptions.SectionName, o => o.AutoCheckOnStartup = value);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        try
        {
            UpdateStatus = "Проверяем обновления…";
            var result = await _updateCoordinator.CheckAndPromptAsync(respectSkippedVersion: false);
            UpdateStatus = result.Message;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenLog() => _shellLauncher.RevealInExplorer(RubricaPaths.LogsDirectory);
}
