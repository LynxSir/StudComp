using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using StudComp.Infrastructure.Notifications;

namespace StudComp.Services;

/// <summary>
/// Иконка в трее на базе <c>Hardcodet.NotifyIcon.Wpf</c> (ARCHITECTURE §11.5). Владеет
/// <see cref="TaskbarIcon"/> сама, не привязываясь к времени жизни окна — приложение продолжает
/// работать в фоне, когда главное окно скрыто.
/// </summary>
internal sealed class TrayService(ILogger<TrayService> logger) : ITrayService, IDisposable
{
    private TaskbarIcon? _icon;

    public event EventHandler? ExitRequested;

    public event EventHandler? CheatSheetRequested;

    public void Initialize()
    {
        if (_icon is not null)
        {
            return;
        }

        var openItem = new MenuItem { Header = "Открыть Rubrica" };
        openItem.Click += (_, _) => ShowMainWindow();

        var cheatSheetItem = new MenuItem { Header = "Шпаргалка" };
        cheatSheetItem.Click += (_, _) => CheatSheetRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new MenuItem { Header = "Выход" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _icon = new TaskbarIcon
        {
            ToolTipText = "Rubrica",
            Icon = LoadTrayIcon(),
            ContextMenu = new ContextMenu { Items = { openItem, cheatSheetItem, exitItem } },
        };
        _icon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        logger.LogInformation("Иконка в трее создана");
    }

    public void ShowMainWindow()
    {
        var window = Application.Current?.MainWindow;
        if (window is null)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }

    public void RequestCheatSheet() => CheatSheetRequested?.Invoke(this, EventArgs.Empty);

    public void Notify(string title, string message, ToastKind kind = ToastKind.Info)
    {
        var icon = kind switch
        {
            ToastKind.Warning => BalloonIcon.Warning,
            ToastKind.Error => BalloonIcon.Error,
            _ => BalloonIcon.Info,
        };

        _icon?.ShowBalloonTip(title, message, icon);
        logger.LogInformation("Уведомление из трея [{Kind}]: {Title} — {Message}", kind, title, message);
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    /// <summary>
    /// Брендовая иконка из ресурсов сборки (new_addons.md §1.7); фолбэк — иконка из собственного exe,
    /// затем системная. Падать из-за иконки трея нельзя.
    /// </summary>
    private static Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/Branding/Rubrica.ico", UriKind.Absolute);
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream is not null)
            {
                using (stream)
                {
                    return new Icon(stream);
                }
            }
        }
        catch (Exception)
        {
            // упадём на фолбэк ниже
        }

        try
        {
            var path = Environment.ProcessPath;
            if (path is not null && Icon.ExtractAssociatedIcon(path) is { } extracted)
            {
                return extracted;
            }
        }
        catch (Exception)
        {
            // уходим на системную
        }

        return SystemIcons.Application;
    }
}
