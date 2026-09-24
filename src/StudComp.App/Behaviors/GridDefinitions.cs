using System.Windows;
using System.Windows.Controls;

namespace StudComp.Behaviors;

/// <summary>
/// Генерирует строки и колонки <see cref="Grid"/> по числу, а не перечислением в разметке.
/// </summary>
/// <remarks>
/// Заведено ради сетки расписания: там 26 получасовых строк, и раньше они были выписаны в XAML
/// руками — вместе с продублированным гаттером часов и захардкоженной высотой полотна. Attached-
/// свойство, а не code-behind, потому что полотно плиток живёт внутри <c>ItemsPanelTemplate</c>,
/// где у <see cref="Grid"/> нет доступного по имени экземпляра.
/// </remarks>
public static class GridDefinitions
{
    /// <summary>Сколько одинаковых строк создать.</summary>
    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.RegisterAttached(
            "Rows", typeof(int), typeof(GridDefinitions), new PropertyMetadata(0, OnRowsChanged));

    /// <summary>Сколько одинаковых колонок создать.</summary>
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.RegisterAttached(
            "Columns", typeof(int), typeof(GridDefinitions), new PropertyMetadata(0, OnColumnsChanged));

    public static int GetRows(DependencyObject element) => (int)element.GetValue(RowsProperty);

    public static void SetRows(DependencyObject element, int value) => element.SetValue(RowsProperty, value);

    public static int GetColumns(DependencyObject element) => (int)element.GetValue(ColumnsProperty);

    public static void SetColumns(DependencyObject element, int value) =>
        element.SetValue(ColumnsProperty, value);

    private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid || e.NewValue is not int count)
        {
            return;
        }

        grid.RowDefinitions.Clear();
        for (var i = 0; i < count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
        }
    }

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid || e.NewValue is not int count)
        {
            return;
        }

        grid.ColumnDefinitions.Clear();
        for (var i = 0; i < count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }
    }
}
