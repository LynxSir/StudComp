using System.Windows;
using Microsoft.Extensions.Logging;
using StudComp.Infrastructure.Notifications;
using StudComp.ViewModels.Shell;

namespace StudComp.Services;

/// <summary>
/// <see cref="IToastService"/> с двумя каналами: пока окно на экране — карточка во внутреннем
/// <see cref="ToastHostViewModel"/> (в том числе с кнопкой-действием, UX «как отмена в Gmail»);
/// когда окно спрятано в трей — балун иконки трея (ADR §16.19). Всё дублируется в лог.
/// </summary>
internal sealed class ToastService(
    ITrayService tray,
    ToastHostViewModel host,
    ILogger<ToastService> logger) : IToastService
{
    public void Show(string title, string message, ToastKind kind = ToastKind.Info) =>
        Route(title, message, kind, actionLabel: null, action: null);

    public void ShowAction(
        string title, string message, string actionLabel, Func<Task> action, ToastKind kind = ToastKind.Info) =>
        Route(title, message, kind, actionLabel, action);

    private void Route(string title, string message, ToastKind kind, string? actionLabel, Func<Task>? action)
    {
        var level = kind switch
        {
            ToastKind.Error => LogLevel.Error,
            ToastKind.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };
        logger.Log(level, "TOAST [{Kind}] {Title}: {Message}", kind, title, message);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // Ни окна, ни трея (например, ранний старт или тесты) — остаётся только лог.
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Deliver(title, message, kind, actionLabel, action);
        }
        else
        {
            dispatcher.InvokeAsync(() => Deliver(title, message, kind, actionLabel, action));
        }
    }

    private void Deliver(string title, string message, ToastKind kind, string? actionLabel, Func<Task>? action)
    {
        var window = Application.Current?.MainWindow;
        if (window is { IsVisible: true })
        {
            host.Push(title, message, kind, actionLabel, action);
        }
        else
        {
            // Балун кнопку не покажет — но хотя бы донесёт текст, пока окно свёрнуто в трей.
            tray.Notify(title, message, kind);
        }
    }
}
