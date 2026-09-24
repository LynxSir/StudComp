using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StudComp.Core.Domain;
using StudComp.ViewModels.Archivist;

namespace StudComp.Behaviors;

/// <summary>
/// Перетаскивание файлов «Неразобранного» на чип предмета — разложить туда (new_addons.md §4). В отличие
/// от <see cref="ListBoxDragReorderBehavior"/> это не reorder внутри одного списка, а перенос между
/// разными визуальными элементами: источник (<see cref="IsDragSourceProperty"/> на <see cref="ListBox"/>
/// с файлами) не теряет элемент, только сообщает о переносе (<see cref="DragDropEffects.Copy"/>);
/// цель (<see cref="DropCommandProperty"/> на чипе предмета) получает готовый
/// <see cref="DropPayload"/> — предмет чипа плюс перетащенные строки.
/// </summary>
public static class DragDropSortBehavior
{
    private const string DataFormat = "StudComp.Archivist.UnsortedRows";

    public static readonly DependencyProperty IsDragSourceProperty = DependencyProperty.RegisterAttached(
        "IsDragSource", typeof(bool), typeof(DragDropSortBehavior), new PropertyMetadata(false, OnIsDragSourceChanged));

    public static readonly DependencyProperty DropCommandProperty = DependencyProperty.RegisterAttached(
        "DropCommand", typeof(ICommand), typeof(DragDropSortBehavior), new PropertyMetadata(null, OnDropCommandChanged));

    private static readonly Dictionary<ListBox, Point> DragOrigins = [];

    public static void SetIsDragSource(DependencyObject element, bool value) => element.SetValue(IsDragSourceProperty, value);

    public static bool GetIsDragSource(DependencyObject element) => (bool)element.GetValue(IsDragSourceProperty);

    public static void SetDropCommand(DependencyObject element, ICommand? value) => element.SetValue(DropCommandProperty, value);

    public static ICommand? GetDropCommand(DependencyObject element) => (ICommand?)element.GetValue(DropCommandProperty);

    /// <summary>Полезная нагрузка сброса: чей чип принял файлы и какие строки перетащены.</summary>
    public sealed record DropPayload(Subject Subject, IReadOnlyList<UnsortedFileRowViewModel> Rows);

    private static void OnIsDragSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        if (e.NewValue is true)
        {
            listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            listBox.PreviewMouseMove += OnPreviewMouseMove;
        }
        else
        {
            DragOrigins.Remove(listBox);
            listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            listBox.PreviewMouseMove -= OnPreviewMouseMove;
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        DragOrigins[(ListBox)sender] = e.GetPosition(null);

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var listBox = (ListBox)sender;
        if (e.LeftButton != MouseButtonState.Pressed || !DragOrigins.TryGetValue(listBox, out var origin))
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var rows = listBox.SelectedItems.Cast<UnsortedFileRowViewModel>().ToList();
        if (rows.Count == 0)
        {
            // Выделения нет — тащим одну строку под курсором.
            var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (container?.DataContext is UnsortedFileRowViewModel row)
            {
                rows = [row];
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        var data = new DataObject(DataFormat, rows);
        DragDrop.DoDragDrop(listBox, data, DragDropEffects.Copy);
    }

    private static void OnDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.PreviewDragOver -= OnPreviewDragOver;
        element.Drop -= OnDrop;

        if (e.NewValue is not null)
        {
            element.AllowDrop = true;
            element.PreviewDragOver += OnPreviewDragOver;
            element.Drop += OnDrop;
        }
    }

    private static void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element
            || GetDropCommand(element) is not { } command
            || element.DataContext is not Subject subject
            || e.Data.GetData(DataFormat) is not IReadOnlyList<UnsortedFileRowViewModel> rows
            || rows.Count == 0)
        {
            return;
        }

        var payload = new DropPayload(subject, rows);
        if (command.CanExecute(payload))
        {
            command.Execute(payload);
        }

        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
