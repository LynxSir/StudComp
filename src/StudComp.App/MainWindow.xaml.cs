using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Services;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp;

/// <summary>
/// Главное окно — оболочка (Shell). Логики почти нет (ARCHITECTURE §2): установка
/// <see cref="FrameworkElement.DataContext"/>, поведение «закрытие → в трей» и персист положения окна
/// (new_addons.md §1.2).
/// </summary>
public partial class MainWindow
{
    private readonly IOptionsMonitor<GeneralOptions> _general;
    private readonly UserSettingsProvider _settings;
    private bool _allowClose;

    public MainWindow(
        MainWindowViewModel viewModel,
        IOptionsMonitor<GeneralOptions> general,
        UserSettingsProvider settings,
        ITrayService tray,
        Wpf.Ui.IContentDialogService contentDialogService)
    {
        _general = general;
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;

        // Подключаем хост модальных диалогов к контролу в разметке (ARCHITECTURE §11.5).
        contentDialogService.SetDialogHost(RootContentDialog);

        // «Выход» из меню трея — единственный путь к настоящему закрытию окна.
        tray.ExitRequested += (_, _) =>
        {
            _allowClose = true;
            Close();
        };

        // Полноэкранный режим сессии: рельс уезжает влево. Длительность берётся из Motion, поэтому
        // при выключенных анимациях (MotionService обнуляет длительности) смена мгновенная.
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsImmersive))
            {
                AnimateRail(viewModel.IsImmersive);
            }
        };

        ApplyPlacement(general.CurrentValue.WindowPlacement);
        UpdateMaxRestoreIcon();

        // Пишем при смене состояния и на закрытии — не на каждый пиксель перетаскивания, чтобы не
        // дёргать usersettings.json и его reloadOnChange.
        StateChanged += (_, _) =>
        {
            UpdateMaxRestoreIcon();
            PersistPlacement();
        };
    }

    /// <summary>Ширина иконочного рельса в обычном режиме.</summary>
    private const double RailWidth = 66;

    private void AnimateRail(bool hidden)
    {
        var target = hidden ? 0 : RailWidth;
        var duration = Motion.Fast;

        if (duration.TimeSpan <= TimeSpan.Zero)
        {
            Rail.BeginAnimation(WidthProperty, null);
            Rail.Width = target;
            return;
        }

        Rail.BeginAnimation(WidthProperty, new DoubleAnimation
        {
            To = target,
            Duration = duration,
            EasingFunction = Motion.EaseOut,
        });
    }

    // ---- Кастомный титул-бар ---------------------------------------------------------------

    private void TitleBarHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Кнопку успели отпустить до начала перетаскивания — не наша забота.
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaxRestoreClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaxRestoreIcon()
    {
        if (MaxRestoreIcon is not null)
        {
            MaxRestoreIcon.Symbol = WindowState == WindowState.Maximized
                ? SymbolRegular.SquareMultiple24
                : SymbolRegular.Maximize24;
        }
    }

    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        PersistPlacement();

        if (!_allowClose && _general.CurrentValue.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Запуск развёрнутым по умолчанию (в т. ч. при первом запуске); нормальные границы восстанавливаются
    /// только если они разумны и попадают на экран.
    /// </summary>
    private void ApplyPlacement(WindowPlacementInfo? placement)
    {
        if (placement is null || !string.Equals(placement.State, "Normal", StringComparison.OrdinalIgnoreCase))
        {
            WindowState = WindowState.Maximized;
            return;
        }

        if (placement.Width >= MinWidth && placement.Height >= MinHeight && IsOnScreen(placement))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
            Width = placement.Width;
            Height = placement.Height;
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private static bool IsOnScreen(WindowPlacementInfo placement)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        // Достаточно, чтобы левый верхний угол с запасом попадал в объединённую область экранов.
        return placement.Left >= virtualLeft - 8 && placement.Left <= virtualRight - 80
            && placement.Top >= virtualTop - 8 && placement.Top <= virtualBottom - 40;
    }

    private void PersistPlacement()
    {
        if (!IsLoaded || WindowState == WindowState.Minimized)
        {
            return;
        }

        var info = WindowState == WindowState.Maximized
            ? new WindowPlacementInfo
            {
                State = "Maximized",
                Left = RestoreBounds.Left,
                Top = RestoreBounds.Top,
                Width = RestoreBounds.Width,
                Height = RestoreBounds.Height,
            }
            : new WindowPlacementInfo
            {
                State = "Normal",
                Left = Left,
                Top = Top,
                Width = ActualWidth,
                Height = ActualHeight,
            };

        _settings.Update<GeneralOptions>(GeneralOptions.SectionName, o => o.WindowPlacement = info);
    }
}
