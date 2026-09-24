namespace StudComp.Infrastructure.Notifications;

/// <summary>Характер уведомления — влияет на иконку/акцент тоста.</summary>
public enum ToastKind
{
    /// <summary>Нейтральное информационное сообщение.</summary>
    Info = 0,

    /// <summary>Успешно завершённая операция.</summary>
    Success = 1,

    /// <summary>Предупреждение, требующее внимания.</summary>
    Warning = 2,

    /// <summary>Ошибка операции.</summary>
    Error = 3,
}

/// <summary>
/// Единая точка показа всплывающих уведомлений (ARCHITECTURE §9.5, §11.5): дедлайны, начало пары,
/// «файл отсортирован», «отчёт сгенерирован» — всё идёт сюда.
/// </summary>
/// <remarks>
/// Реализация в <c>StudComp.App</c> маршрутизирует уведомление в balloon иконки трея
/// (<see cref="ITrayService.Notify"/>) и дублирует в лог. Настоящие Windows Toast
/// (<c>CommunityToolkit.WinUI.Notifications</c> + регистрация AppUserModelID) отложены до Phase 13,
/// когда появится ярлык в меню «Пуск» (ADR §16.19).
/// </remarks>
public interface IToastService
{
    /// <summary>Показать уведомление.</summary>
    void Show(string title, string message, ToastKind kind = ToastKind.Info);

    /// <summary>
    /// Показать уведомление с одной кнопкой-действием (UX «как отмена архивирования в Gmail»):
    /// например «Файл отсортирован · Отменить». <paramref name="action"/> выполняется на UI-потоке.
    /// Если интерактивный тост показать негде (окна ещё/уже нет), деградирует до обычного
    /// <see cref="Show"/> без кнопки.
    /// </summary>
    void ShowAction(
        string title, string message, string actionLabel, Func<Task> action, ToastKind kind = ToastKind.Info);
}
