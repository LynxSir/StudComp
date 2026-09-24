using StudComp.Core.Common;

namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Пути к пользовательской конфигурации (ARCHITECTURE §6, §11.2). Тонкий фасад над
/// <see cref="RubricaPaths"/> — имя <c>UserSettingsPaths</c> фигурирует в эскизе composition root.
/// </summary>
public static class UserSettingsPaths
{
    /// <summary>
    /// Файл-оверлей пользовательских настроек поверх заводского <c>appsettings.json</c>:
    /// <c>%LocalAppData%\Rubrica\usersettings.json</c>.
    /// </summary>
    public static string UserSettingsFile => RubricaPaths.UserSettingsFile;

    /// <summary>Корень раздела настроек продукта в конфигурации — <c>"Rubrica"</c>.</summary>
    public const string RootSection = "Rubrica";
}
