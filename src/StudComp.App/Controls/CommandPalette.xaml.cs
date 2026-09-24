using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StudComp.Services;
using StudComp.ViewModels.Cards;

namespace StudComp.Controls;

/// <summary>
/// Оверлей глобального поиска по картотеке — <c>Ctrl+K</c> (new_addons.md §4.6).
/// </summary>
/// <remarks>
/// Приём тот же, что у модалки настроек (12.5): скрим плюс панель, короткая анимация появления,
/// закрытие по <c>Esc</c> и по клику мимо. Отличие одно, но важное: оверлей запоминает, где был
/// фокус до открытия, и возвращает его на место — <c>Ctrl+K</c> вызывают посреди работы, и
/// потерять из-за него курсор в поле ввода было бы обидно.
/// </remarks>
public partial class CommandPalette : UserControl
{
    private CardPaletteViewModel? _viewModel;
    private IInputElement? _focusBeforeOpen;

    public CommandPalette()
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

        _viewModel = e.NewValue as CardPaletteViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncVisibility(animate: false);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardPaletteViewModel.IsOpen))
        {
            SyncVisibility(animate: true);
        }
    }

    private void SyncVisibility(bool animate)
    {
        if (_viewModel?.IsOpen == true)
        {
            _focusBeforeOpen = Keyboard.FocusedElement;
            Visibility = Visibility.Visible;

            Dispatcher.BeginInvoke(() =>
            {
                Input.Focus();
                Input.SelectAll();
            });

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

            return;
        }

        RestoreFocus();

        if (animate)
        {
            PlayClose();
        }
        else
        {
            Visibility = Visibility.Collapsed;
        }
    }

    private void RestoreFocus()
    {
        if (_focusBeforeOpen is null)
        {
            return;
        }

        var target = _focusBeforeOpen;
        _focusBeforeOpen = null;
        Dispatcher.BeginInvoke(() => Keyboard.Focus(target));
    }

    private void OnKeyDown(object sender, KeyEventArgs e) => HandleKey(e);

    private void OnInputKeyDown(object sender, KeyEventArgs e) => HandleKey(e);

    private void HandleKey(KeyEventArgs e)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                vm.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                vm.SelectNextCommand.Execute(null);
                ScrollToSelection();
                e.Handled = true;
                break;

            case Key.Up:
                vm.SelectPreviousCommand.Execute(null);
                ScrollToSelection();
                e.Handled = true;
                break;

            case Key.Enter:
                vm.OpenSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Space when Keyboard.FocusedElement is not TextBox:
                vm.ToggleAnswerCommand.Execute(null);
                e.Handled = true;
                break;

            // Пробел в строке ввода — обычный пробел; ответ раскрывается сочетанием с Ctrl.
            case Key.Space when (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                vm.ToggleAnswerCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ScrollToSelection()
    {
        if (_viewModel?.Selected is { } row)
        {
            ResultsList.ScrollIntoView(row);
        }
    }

    private void OnScrimClick(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Scrim))
        {
            _viewModel?.CloseCommand.Execute(null);
        }
    }

    private void PlayOpen()
    {
        var duration = Motion.Fast;
        var ease = Motion.EaseOut;

        Scrim.BeginAnimation(OpacityProperty, Fade(0, 1, duration, ease));
        Panel.BeginAnimation(OpacityProperty, Fade(0, 1, duration, ease));
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, Fade(0.97, 1, duration, ease));
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, Fade(0.97, 1, duration, ease));
    }

    private void PlayClose()
    {
        var duration = Motion.Fast;
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
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, Fade(1, 0.98, duration, ease));
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, Fade(1, 0.98, duration, ease));
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
