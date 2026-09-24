using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>Раздел «Общие» (new_addons.md §7 §1): трей при закрытии, язык, формат даты.</summary>
public sealed partial class GeneralSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly UserSettingsProvider _settings;

    public GeneralSettingsViewModel(UserSettingsProvider settings, IOptions<GeneralOptions> general)
    {
        _settings = settings;

        using (BeginLoad())
        {
            _minimizeToTrayOnClose = general.Value.MinimizeToTrayOnClose;
            _selectedDateFormat = SettingsChoices.DateFormats.FirstOrDefault(x => x.Value == general.Value.DateFormat)
                ?? SettingsChoices.DateFormats[0];
        }
    }

    public override SettingsSection Section => SettingsSection.General;

    public override string Title => "Общие";

    public override SymbolRegular Icon => SymbolRegular.Settings24;

    public override IEnumerable<string> SearchKeywords =>
        ["трей", "закрытие", "свернуть", "язык", "русский", "локализация", "формат даты", "развёрнутое окно"];

    /// <summary>Варианты формата даты.</summary>
    public IReadOnlyList<DateFormatChoice> DateFormats => SettingsChoices.DateFormats;

    /// <summary>Язык интерфейса — пока только русский (задел под локализацию).</summary>
    public string LanguageDisplay => "Русский";

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private DateFormatChoice _selectedDateFormat;

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (!IsLoading)
        {
            _settings.Update<GeneralOptions>(GeneralOptions.SectionName, o => o.MinimizeToTrayOnClose = value);
        }
    }

    partial void OnSelectedDateFormatChanged(DateFormatChoice value)
    {
        if (!IsLoading && value is not null)
        {
            _settings.Update<GeneralOptions>(GeneralOptions.SectionName, o => o.DateFormat = value.Value);
        }
    }
}
