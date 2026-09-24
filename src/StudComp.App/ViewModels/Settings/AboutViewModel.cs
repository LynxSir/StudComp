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

    private AppUpdateInfo? _foundUpdate;

    public AboutViewModel(
        UserSettingsProvider settings,
        IShellLauncher shellLauncher,
        IUpdateService updates,
        IOptions<DiagnosticsOptions> diagnostics,
        IOptions<UpdateOptions> update)
    {
        _settings = settings;
        _shellLauncher = shellLauncher;
        _updates = updates;

        using (BeginLoad())
        {
            _performanceLoggingEnabled = diagnostics.Value.PerformanceLoggingEnabled;
            _autoCheckOnStartup = update.Value.AutoCheckOnStartup;
            _githubToken = update.Value.GithubToken;
            _updateStatus = updates.IsUpdateSupported
                ? "Нажмите «Проверить», чтобы узнать о новой версии."
                : "Проверка обновлений доступна только в установленной версии.";
        }
    }

    public override SettingsSection Section => SettingsSection.About;

    public override string Title => "О программе";

    public override SymbolRegular Icon => SymbolRegular.Info24;

    public override IEnumerable<string> SearchKeywords =>
        ["о программе", "версия", "обновления", "обновить", "лицензия", "диагностика", "производительность", "лог"];

    public string ProductName => "Rubrica";

    public string Version => _updates.CurrentVersion;

    public string LicenseText => "Приватный проект. Все права принадлежат автору.";

    [ObservableProperty]
    private bool _performanceLoggingEnabled;

    [ObservableProperty]
    private bool _autoCheckOnStartup;

    [ObservableProperty]
    private string _githubToken;

    [ObservableProperty]
    private string _updateStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadAndApplyCommand))]
    private bool _updateAvailable;

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

    [RelayCommand]
    private void SaveToken()
    {
        _settings.Update<UpdateOptions>(UpdateOptions.SectionName, o => o.GithubToken = GithubToken?.Trim() ?? string.Empty);
        UpdateStatus = "Токен сохранён. Нажмите «Проверить».";
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (!_updates.IsUpdateSupported)
        {
            UpdateStatus = "Проверка обновлений доступна только в установленной версии.";
            return;
        }

        UpdateStatus = "Проверяем…";
        UpdateAvailable = false;
        try
        {
            _foundUpdate = await _updates.CheckAsync();
            if (_foundUpdate is null)
            {
                UpdateStatus = "Установлена последняя версия.";
                return;
            }

            UpdateAvailable = true;
            UpdateStatus = $"Доступна версия {_foundUpdate.Version}.";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Не удалось проверить обновления: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(UpdateAvailable))]
    private async Task DownloadAndApplyAsync()
    {
        if (_foundUpdate is null)
        {
            return;
        }

        try
        {
            UpdateStatus = "Скачиваем обновление…";
            await _updates.DownloadAsync(_foundUpdate);
            UpdateStatus = "Перезапуск…";
            _updates.ApplyAndRestart(_foundUpdate);
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Не удалось установить обновление: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenLog() => _shellLauncher.RevealInExplorer(RubricaPaths.LogsDirectory);
}
