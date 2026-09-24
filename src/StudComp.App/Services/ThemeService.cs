using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;
using Wpf.Ui.Appearance;

namespace StudComp.Services;

/// <inheritdoc cref="IThemeService"/>
internal sealed class ThemeService(
    IOptionsMonitor<AppearanceOptions> appearance,
    ILogger<ThemeService> logger) : IThemeService
{
    private Window? _window;
    private bool _followingSystem;
    private AppTheme _current;

    /// <summary>
    /// Окно нужно только слежению за системной темой. Тема применяется <b>до</b> создания окна
    /// (Phase 13.10: словари темы подменяются на пустом дереве, а не на живом), поэтому если к
    /// моменту вызова уже выбрано «как в системе», слежение включается здесь.
    /// </summary>
    public void Initialize(Window window)
    {
        _window = window;
        if (_current == AppTheme.System && !_followingSystem)
        {
            SystemThemeWatcher.Watch(_window);
            _followingSystem = true;
        }
    }

    public void Apply(AppTheme theme)
    {
        _current = theme;
        switch (theme)
        {
            case AppTheme.Light:
                StopFollowingSystem();
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;

            case AppTheme.Dark:
                StopFollowingSystem();
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;

            default:
                ApplicationThemeManager.ApplySystemTheme();
                if (_window is not null && !_followingSystem)
                {
                    SystemThemeWatcher.Watch(_window);
                    _followingSystem = true;
                }

                break;
        }

        // Акцент из настроек (new_addons.md §7 §2) — при смене темы его нужно применить заново
        // поверх новой темы WPF-UI. Пресет/hex разбирает AccentPalette.
        ApplicationAccentColorManager.Apply(
            AccentPalette.Resolve(appearance.CurrentValue.Accent), ApplicationThemeManager.GetAppTheme());
        logger.LogInformation("Тема применена: {Theme}", theme);
    }

    private void StopFollowingSystem()
    {
        // UnWatch кидает, если окно не watched или ещё не загружено — снимаем слежение только когда
        // сами его ставили (напр. дефолтная тема — светлая, слежение вообще не включалось).
        if (_window is not null && _followingSystem)
        {
            SystemThemeWatcher.UnWatch(_window);
            _followingSystem = false;
        }
    }
}
