namespace StudComp.Infrastructure.Notifications;

/// <summary>
/// Иконка в системном трее (ARCHITECTURE §11.5): приложение сворачивается в трей вместо завершения,
/// чтобы фоновые сервисы (Архивариус, напоминания) продолжали работать.
/// </summary>
/// <remarks>Реализация — в <c>StudComp.App</c> (нужен WPF); здесь только контракт.</remarks>
public interface ITrayService
{
    /// <summary>Создаёт иконку в трее и её контекст-меню. Вызывается один раз при старте.</summary>
    void Initialize();

    /// <summary>Показать и активировать главное окно (клик по иконке / пункт «Открыть»).</summary>
    void ShowMainWindow();

    /// <summary>
    /// Короткое всплывающее сообщение (balloon) от иконки трея. В Phase 4 это единственный канал
    /// тост-напоминаний Органайзера; настоящие Windows Toast — Phase 13 (ARCHITECTURE §9.5, ADR §16.19).
    /// </summary>
    void Notify(string title, string message, ToastKind kind = ToastKind.Info);

    /// <summary>Пользователь выбрал «Выход» в меню трея — хосту нужно завершить приложение полностью.</summary>
    event EventHandler? ExitRequested;

    /// <summary>
    /// Пользователь выбрал «Шпаргалка» в меню трея (new_addons.md §8.6) — хост показывает компактное
    /// окно картотеки. Трей само окно не знает, поэтому только просит.
    /// </summary>
    event EventHandler? CheatSheetRequested;

    /// <summary>
    /// Попросить показать компактный режим-шпаргалку — тот же запрос, что и пункт меню трея, но из
    /// кнопки внутри раздела «Картотека» (new_addons.md §8.6): VM не обязан знать про окно, чтобы его открыть.
    /// </summary>
    void RequestCheatSheet();
}
