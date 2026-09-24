using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;
using StudComp.Services;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Раздел «Оформление» (new_addons.md §7 §2): тема, акцент (пресеты + свой), плотность, масштаб,
/// анимации. Всё применяется немедленно через <see cref="IThemeService"/> / <see cref="IMotionService"/>.
/// </summary>
public sealed partial class AppearanceSettingsViewModel : SettingsSectionViewModelBase
{
    private static readonly Regex HexColor = new("^#?[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private readonly UserSettingsProvider _settings;
    private readonly IThemeService _theme;
    private readonly IMotionService _motion;

    public AppearanceSettingsViewModel(
        UserSettingsProvider settings,
        IThemeService theme,
        IMotionService motion,
        IOptions<AppearanceOptions> appearance)
    {
        _settings = settings;
        _theme = theme;
        _motion = motion;

        using (BeginLoad())
        {
            var value = appearance.Value;
            _selectedTheme = SettingsChoices.Themes.FirstOrDefault(x => x.Value == value.Theme) ?? SettingsChoices.Themes[0];
            _selectedDensity = SettingsChoices.Densities.FirstOrDefault(x => x.Value == value.Density) ?? SettingsChoices.Densities[0];
            _selectedFontScale = SettingsChoices.FontScales.FirstOrDefault(x => Math.Abs(x.Value - value.FontScale) < 0.001)
                ?? SettingsChoices.FontScales.First(x => Math.Abs(x.Value - 1.0) < 0.001);
            _animationsEnabled = value.AnimationsEnabled;

            var preset = SettingsChoices.AccentPresets.FirstOrDefault(
                x => string.Equals(x.Key, value.Accent, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
            {
                _selectedAccent = preset;
                _useCustomAccent = false;
                _customAccentHex = SettingsChoices.AccentPresets[0].Hex;
            }
            else
            {
                _selectedAccent = SettingsChoices.AccentPresets[0];
                _useCustomAccent = true;
                _customAccentHex = HexColor.IsMatch(value.Accent) ? Normalize(value.Accent) : "#8A1C2B";
            }
        }
    }

    public override SettingsSection Section => SettingsSection.Appearance;

    public override string Title => "Оформление";

    public override SymbolRegular Icon => SymbolRegular.Color24;

    public override IEnumerable<string> SearchKeywords =>
        ["тема", "светлая", "тёмная", "акцент", "цвет", "плотность", "компактная", "масштаб", "размер", "анимации", "переходы"];

    public IReadOnlyList<ThemeChoice> Themes => SettingsChoices.Themes;

    public IReadOnlyList<DensityChoice> Densities => SettingsChoices.Densities;

    public IReadOnlyList<FontScaleChoice> FontScales => SettingsChoices.FontScales;

    public IReadOnlyList<AccentChoice> AccentPresets => SettingsChoices.AccentPresets;

    [ObservableProperty]
    private ThemeChoice _selectedTheme;

    [ObservableProperty]
    private AccentChoice _selectedAccent;

    [ObservableProperty]
    private bool _useCustomAccent;

    [ObservableProperty]
    private string _customAccentHex;

    [ObservableProperty]
    private DensityChoice _selectedDensity;

    [ObservableProperty]
    private FontScaleChoice _selectedFontScale;

    [ObservableProperty]
    private bool _animationsEnabled;

    partial void OnSelectedThemeChanged(ThemeChoice value)
    {
        if (value is null)
        {
            return;
        }

        _theme.Apply(value.Value);
        if (!IsLoading)
        {
            _settings.Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.Theme = value.Value);
        }
    }

    partial void OnSelectedAccentChanged(AccentChoice value)
    {
        if (value is not null && !UseCustomAccent)
        {
            PersistAccent(value.Key);
        }
    }

    partial void OnUseCustomAccentChanged(bool value) => PersistAccent(CurrentAccentValue());

    partial void OnCustomAccentHexChanged(string value)
    {
        if (UseCustomAccent && HexColor.IsMatch(value ?? string.Empty))
        {
            PersistAccent(Normalize(value!));
        }
    }

    partial void OnSelectedDensityChanged(DensityChoice value)
    {
        if (value is null)
        {
            return;
        }

        _motion.ApplyDensity(value.Value);
        if (!IsLoading)
        {
            _settings.Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.Density = value.Value);
        }
    }

    partial void OnSelectedFontScaleChanged(FontScaleChoice value)
    {
        if (value is null)
        {
            return;
        }

        _motion.ApplyFontScale(value.Value);
        if (!IsLoading)
        {
            _settings.Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.FontScale = value.Value);
        }
    }

    partial void OnAnimationsEnabledChanged(bool value)
    {
        _motion.ApplyAnimations(value);
        if (!IsLoading)
        {
            _settings.Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.AnimationsEnabled = value);
        }
    }

    private string CurrentAccentValue() =>
        UseCustomAccent && HexColor.IsMatch(CustomAccentHex ?? string.Empty)
            ? Normalize(CustomAccentHex!)
            : SelectedAccent?.Key ?? AppearanceOptions.DefaultAccent;

    private void PersistAccent(string accent)
    {
        _motion.ApplyAccent(accent);
        if (!IsLoading)
        {
            _settings.Update<AppearanceOptions>(AppearanceOptions.SectionName, o => o.Accent = accent);
        }
    }

    private static string Normalize(string hex) => hex.StartsWith('#') ? hex.ToUpperInvariant() : "#" + hex.ToUpperInvariant();
}
