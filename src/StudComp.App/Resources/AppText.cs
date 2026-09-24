using System.Globalization;
using System.Resources;

namespace StudComp.Resources;

/// <summary>
/// Типизированный доступ к строкам уровня приложения из <c>Resources/AppText.resx</c>.
/// Скелет локализации, заведён в Phase 12 — см. комментарий в <c>StudComp.App.csproj</c>.
/// </summary>
internal static class AppText
{
    private static readonly ResourceManager Manager =
        new("StudComp.Resources.AppText", typeof(AppText).Assembly);

    /// <summary>Заголовок окон и диалогов приложения.</summary>
    public static string AppTitle => Get(nameof(AppTitle));

    /// <summary>Текст <c>MessageBox</c> при фатальном сбое во время запуска.</summary>
    public static string StartupFailed => Get(nameof(StartupFailed));

    /// <summary>Текст <c>MessageBox</c> при необработанном исключении в UI-потоке.</summary>
    public static string UnhandledUiError => Get(nameof(UnhandledUiError));

    private static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
