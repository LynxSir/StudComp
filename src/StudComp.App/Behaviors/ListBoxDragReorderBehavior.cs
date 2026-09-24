using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StudComp.Behaviors;

/// <summary>
/// Перетаскивание строк <see cref="ListBox"/> для смены порядка (ARCHITECTURE §8.7, drag-n-drop
/// приоритета). Двигает элементы прямо в привязанной <see cref="IList"/> и после дропа выполняет
/// <see cref="ReorderCommandProperty"/> — ViewModel по нему фиксирует новый порядок.
/// Написано с нуля: готового механизма reorder в проекте нет, тащить пакет ради одного списка незачем.
/// </summary>
public static class ListBoxDragReorderBehavior
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable", typeof(bool), typeof(ListBoxDragReorderBehavior),
        new PropertyMetadata(false, OnEnableChanged));

    public static readonly DependencyProperty ReorderCommandProperty = DependencyProperty.RegisterAttached(
        "ReorderCommand", typeof(ICommand), typeof(ListBoxDragReorderBehavior));

    private static readonly Dictionary<ListBox, DragState> States = [];

    public static void SetEnable(DependencyObject element, bool value) =>
        element.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    public static void SetReorderCommand(DependencyObject element, ICommand value) =>
        element.SetValue(ReorderCommandProperty, value);

    public static ICommand? GetReorderCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ReorderCommandProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        if (e.NewValue is true)
        {
            States[listBox] = new DragState();
            listBox.AllowDrop = true;
            listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            listBox.PreviewMouseMove += OnPreviewMouseMove;
            listBox.DragOver += OnDragOver;
            listBox.Drop += OnDrop;
            listBox.Unloaded += OnUnloaded;
        }
        else if (States.Remove(listBox))
        {
            Detach(listBox);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox && States.Remove(listBox))
        {
            Detach(listBox);
        }
    }

    private static void Detach(ListBox listBox)
    {
        listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove -= OnPreviewMouseMove;
        listBox.DragOver -= OnDragOver;
        listBox.Drop -= OnDrop;
        listBox.Unloaded -= OnUnloaded;
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var listBox = (ListBox)sender;
        if (!States.TryGetValue(listBox, out var state))
        {
            return;
        }

        state.Origin = e.GetPosition(listBox);
        state.Item = ItemUnder(listBox, e.OriginalSource as DependencyObject);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var listBox = (ListBox)sender;
        if (!States.TryGetValue(listBox, out var state)
            || state.Item is null
            || state.IsDragging
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(listBox);
        if (Math.Abs(current.X - state.Origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - state.Origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        state.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop(listBox, state.Item, DragDropEffects.Move);
        }
        finally
        {
            state.IsDragging = false;
            state.Item = null;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var listBox = (ListBox)sender;
        if (!States.TryGetValue(listBox, out var state)
            || state.Item is null
            || listBox.ItemsSource is not IList list)
        {
            return;
        }

        var dragged = state.Item;
        var targetItem = ItemUnder(listBox, e.OriginalSource as DependencyObject);

        var from = list.IndexOf(dragged);
        var to = targetItem is null ? list.Count - 1 : list.IndexOf(targetItem);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        list.RemoveAt(from);
        list.Insert(to, dragged);

        var command = GetReorderCommand(listBox);
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    private static object? ItemUnder(ListBox listBox, DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source is ListBoxItem container
            ? listBox.ItemContainerGenerator.ItemFromContainer(container)
            : null;
    }

    private sealed class DragState
    {
        public Point Origin { get; set; }

        public object? Item { get; set; }

        public bool IsDragging { get; set; }
    }
}
