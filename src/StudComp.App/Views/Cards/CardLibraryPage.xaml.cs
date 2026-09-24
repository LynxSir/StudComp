using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using StudComp.ViewModels.Cards;

namespace StudComp.Views.Cards;

/// <summary>
/// Вкладка «Библиотека»: три колонки и полная клавиатура (new_addons.md §8.2, §8.4).
/// </summary>
/// <remarks>
/// Код-behind здесь только ради клавиатуры: раскладка команд зависит от того, где сейчас фокус, а
/// это знание чисто визуальное и во вьюмодели ему делать нечего. Тот же приём, что у помощника
/// вставки токенов в редакторе правил Архивариуса (Phase 12.2).
/// </remarks>
public partial class CardLibraryPage : UserControl
{
    public CardLibraryPage()
    {
        InitializeComponent();
    }

    private CardLibraryViewModel? ViewModel => DataContext as CardLibraryViewModel;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var typing = Keyboard.FocusedElement is TextBoxBase;

        // Сочетания с Control работают везде, в том числе прямо во время набора текста.
        if (control)
        {
            switch (e.Key)
            {
                case Key.N:
                    vm.NewCardCommand.Execute(null);
                    e.Handled = true;
                    return;

                case Key.Enter:
                    vm.Preview.EndEditCommand.Execute(null);
                    e.Handled = true;
                    return;

                case Key.A when !typing:
                    vm.SelectAllCommand.Execute(null);
                    e.Handled = true;
                    return;
            }

            return;
        }

        if (typing)
        {
            // Внутри поля ввода живут только Esc и Ctrl-сочетания: остальное это текст.
            if (e.Key == Key.Escape)
            {
                MoveFocusToList();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.OemQuestion or Key.Divide:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;

            case Key.E or Key.Enter:
                vm.EditCardCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F:
                vm.TogglePinCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S:
                vm.ToggleSuspendCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete:
                vm.DeleteCardCommand.Execute(null);
                e.Handled = true;
                break;

            // В режиме списка стрелки влево-вправо свободны — отдаём их переходу по карточкам.
            case Key.Left when !vm.IsGridView:
                vm.SelectPreviousCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Right when !vm.IsGridView:
                vm.SelectNextCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (vm.SelectedCount > 0)
                {
                    vm.ClearSelectionCommand.Execute(null);
                }
                else
                {
                    vm.ClearQueryCommand.Execute(null);
                }

                e.Handled = true;
                break;
        }
    }

    /// <summary>Вернуть фокус в выдачу — из строки поиска по <c>Esc</c>.</summary>
    private void MoveFocusToList()
    {
        var list = ViewModel?.IsGridView == false ? RowList : GridList;
        list.Focus();
    }

    /// <summary>
    /// <see cref="ContextMenu"/> в WPF открывается по правому клику, а кнопкам «Импорт»/«Экспорт»
    /// нужен обычный левый — чисто визуальная деталь, которой не место во вьюмодели.
    /// </summary>
    private void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }
    }
}
