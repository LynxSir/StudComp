using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using StudComp.Services;
using StudComp.ViewModels.Shell;

namespace StudComp.Controls;

/// <summary>
/// Модалка настроек (new_addons.md §7): скрим + центральная панель, рельс разделов слева и View
/// выбранного раздела справа (смена вида). Закрытие по <c>Esc</c>, кнопке «×» и клику на скрим.
/// Открытие/закрытие — короткая анимация (fade + масштаб), гейтится ресурсом длительности.
/// </summary>
public partial class SettingsOverlay : UserControl
{
    private SettingsShellViewModel? _viewModel;

    public SettingsOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as SettingsShellViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncVisibility(animate: false);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsShellViewModel.IsOpen))
        {
            SyncVisibility(animate: true);
        }
    }

    private void SyncVisibility(bool animate)
    {
        if (_viewModel?.IsOpen == true)
        {
            Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(() => Focus());
            if (animate)
            {
                PlayOpen();
            }
            else
            {
                Scrim.Opacity = 1;
                Panel.Opacity = 1;
                PanelScale.ScaleX = PanelScale.ScaleY = 1;
            }
        }
        else if (animate)
        {
            PlayClose();
        }
        else
        {
            Visibility = Visibility.Collapsed;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel?.CloseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void PlayOpen()
    {
        var duration = Motion.Medium;
        var ease = Motion.EaseOut;

        Scrim.BeginAnimation(OpacityProperty, Fade(0, 1, duration, ease));
        Panel.BeginAnimation(OpacityProperty, Fade(0, 1, duration, ease));
        PanelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, Fade(0.97, 1, duration, ease));
        PanelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, Fade(0.97, 1, duration, ease));
    }

    private void PlayClose()
    {
        var duration = Motion.Medium;
        var ease = Motion.EaseOut;

        var panelFade = Fade(Panel.Opacity, 0, duration, ease);
        panelFade.Completed += (_, _) =>
        {
            if (_viewModel?.IsOpen != true)
            {
                Visibility = Visibility.Collapsed;
            }
        };

        Scrim.BeginAnimation(OpacityProperty, Fade(Scrim.Opacity, 0, duration, ease));
        Panel.BeginAnimation(OpacityProperty, panelFade);
        PanelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, Fade(1, 0.98, duration, ease));
        PanelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, Fade(1, 0.98, duration, ease));
    }

    private static DoubleAnimation Fade(double from, double to, Duration duration, IEasingFunction? ease) => new()
    {
        From = from,
        To = to,
        Duration = duration,
        EasingFunction = ease,
        FillBehavior = FillBehavior.HoldEnd,
    };
}
