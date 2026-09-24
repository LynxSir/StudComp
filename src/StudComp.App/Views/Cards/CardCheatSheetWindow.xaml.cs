using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StudComp.ViewModels.Cards;

namespace StudComp.Views.Cards;

/// <summary>
/// Компактный режим-шпаргалка (new_addons.md §8.6): отдельное маленькое окно, всегда поверх
/// остальных. Singleton — состояние поиска переживает <see cref="Hide"/>/<see cref="Show"/> между
/// вызовами, поэтому обычное закрытие (крестик, Alt+F4) не уничтожает окно, а прячет его; настоящее
/// закрытие — только через <see cref="ForceClose"/>, которым явно пользуется выход из приложения.
/// </summary>
public partial class CardCheatSheetWindow : Window
{
    private bool _allowClose;

    public CardCheatSheetWindow(CardCheatSheetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private CardCheatSheetViewModel ViewModel => (CardCheatSheetViewModel)DataContext;

    /// <summary>Показать окно и подвести фокус в строку поиска — вызов из раздела или из трея.</summary>
    public void ShowAndFocus()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        ViewModel.Activate();
        Dispatcher.BeginInvoke(() =>
        {
            Input.Focus();
            Input.SelectAll();
        });
    }

    /// <summary>Переключатель для меню трея и глобальной горячей клавиши.</summary>
    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowAndFocus();
        }
    }

    /// <summary>Настоящее закрытие — только при выходе из приложения.</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;

            case Key.Down:
                ViewModel.SelectNextCommand.Execute(null);
                ScrollToSelection();
                e.Handled = true;
                break;

            case Key.Up:
                ViewModel.SelectPreviousCommand.Execute(null);
                ScrollToSelection();
                e.Handled = true;
                break;

            case Key.Enter:
                ViewModel.ToggleAnswerCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Space when Keyboard.FocusedElement is not TextBox:
                ViewModel.ToggleAnswerCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ScrollToSelection()
    {
        if (ViewModel.Selected is { } row)
        {
            ResultsList.ScrollIntoView(row);
        }
    }
}
