using System.Collections.Concurrent;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using StudComp.Infrastructure.Notifications;

namespace StudComp.Services;

/// <summary>
/// <see cref="IToastService"/> с настоящими Windows-тостами (ADR §16.20) для случая, когда окно
/// скрыто в трей: <c>ToastContentBuilder</c> из <c>Microsoft.Toolkit.Uwp.Notifications</c>,
/// AppUserModelID берётся из ярлыка «Пуска», который создаёт инсталлятор Velopack.
/// </summary>
/// <remarks>
/// Когда окно на экране — уведомление уходит карточкой во внутренний <c>ToastHost</c> (это уже
/// умеет <see cref="ToastService"/>, в него и делегируем). Если системный тост показать не удалось
/// (запуск не из установленной копии, отключены уведомления Windows) — тихий откат на балун трея
/// через тот же <see cref="ToastService"/>. Под F5 поведение не меняется.
/// </remarks>
internal sealed class WindowsToastService : IToastService
{
    private readonly ToastService _fallback;
    private readonly ILogger<WindowsToastService> _logger;
    private readonly ConcurrentDictionary<string, Func<Task>> _pendingActions = new();
    private bool _nativeToastsBroken;

    public WindowsToastService(ToastService fallback, ILogger<WindowsToastService> logger)
    {
        _fallback = fallback;
        _logger = logger;

        try
        {
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }
        catch (Exception ex)
        {
            _nativeToastsBroken = true;
            _logger.LogWarning(ex, "Системные тосты недоступны, используется балун трея");
        }
    }

    public void Show(string title, string message, ToastKind kind = ToastKind.Info)
    {
        if (WindowOnScreen() || !TryShowNative(() => BuildBase(title, message).Show()))
        {
            _fallback.Show(title, message, kind);
        }
    }

    public void ShowAction(
        string title, string message, string actionLabel, Func<Task> action, ToastKind kind = ToastKind.Info)
    {
        if (WindowOnScreen())
        {
            _fallback.ShowAction(title, message, actionLabel, action, kind);
            return;
        }

        var token = Guid.NewGuid().ToString("N");
        _pendingActions[token] = action;

        var shown = TryShowNative(() => BuildBase(title, message)
            .AddButton(new ToastButton()
                .SetContent(actionLabel)
                .AddArgument("action", token)
                .SetBackgroundActivation())
            .Show());

        if (!shown)
        {
            _pendingActions.TryRemove(token, out _);
            _fallback.ShowAction(title, message, actionLabel, action, kind);
        }
    }

    private static ToastContentBuilder BuildBase(string title, string message) =>
        new ToastContentBuilder()
            .AddText(title)
            .AddText(message);

    private static bool WindowOnScreen() =>
        Application.Current?.MainWindow is { IsVisible: true };

    private bool TryShowNative(Action show)
    {
        if (_nativeToastsBroken)
        {
            return false;
        }

        try
        {
            show();
            return true;
        }
        catch (Exception ex)
        {
            _nativeToastsBroken = true;
            _logger.LogWarning(ex, "Не удалось показать системный тост, дальше — балун трея");
            return false;
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        ToastArguments args;
        try
        {
            args = ToastArguments.Parse(e.Argument);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не разобрать аргументы активации тоста");
            return;
        }

        if (!args.TryGetValue("action", out var token) || !_pendingActions.TryRemove(token, out var action))
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Действие тоста завершилось ошибкой");
            }
        });
    }
}
