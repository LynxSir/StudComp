using StudComp.Infrastructure.Settings;

namespace StudComp.Services;

/// <summary>
/// Применение темы оформления поверх WPF-UI (ARCHITECTURE §11.5): light/dark + синхронизация с
/// системной темой Windows. Изолирует статические вызовы WPF-UI от ViewModel'ей.
/// </summary>
public interface IThemeService
{
    /// <summary>Запоминает главное окно — нужно для подписки на смену системной темы.</summary>
    void Initialize(System.Windows.Window window);

    /// <summary>Применяет выбранную тему немедленно. Для <see cref="AppTheme.System"/> — включает слежение.</summary>
    void Apply(AppTheme theme);
}
