using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StudComp.Behaviors;

/// <summary>Клик по пустой ячейке сетки расписания: день недели и номер получасовой строки.</summary>
public sealed record ScheduleCellHit(int DayColumn, int RowIndex);

/// <summary>Плитку перетащили: новый день и новая строка начала.</summary>
public sealed record ScheduleTileMove(object Tile, int DayColumn, int RowIndex);

/// <summary>Плитке потянули нижний край: новая высота в строках сетки.</summary>
public sealed record ScheduleTileResize(object Tile, int RowSpan);

/// <summary>
/// Взаимодействие с полотном расписания (new_addons.md §5): клик по пустому месту, перетаскивание
/// плитки и изменение её длительности за нижний край.
/// </summary>
/// <remarks>
/// Своё поведение вместо 182 контролов-ячеек: пустое место — это одна прозрачная подложка, а
/// «пустая ли ячейка» решается само собой, потому что плитки лежат выше и перехватывают клик.
/// Идиома attached-свойств взята у <c>ListBoxDragReorderBehavior</c> и <c>DragDropSortBehavior</c>.
/// </remarks>
public static class ScheduleGridBehavior
{
    /// <summary>Высота зоны захвата для изменения длительности, px.</summary>
    private const double ResizeGripHeight = 7;

    /// <summary>Команда, получающая <see cref="ScheduleCellHit"/> при клике по пустому месту.</summary>
    public static readonly DependencyProperty EmptyCellCommandProperty =
        DependencyProperty.RegisterAttached(
            "EmptyCellCommand",
            typeof(ICommand),
            typeof(ScheduleGridBehavior),
            new PropertyMetadata(null, OnEmptyCellCommandChanged));

    /// <summary>Команда переноса плитки (<see cref="ScheduleTileMove"/>).</summary>
    public static readonly DependencyProperty MoveCommandProperty =
        DependencyProperty.RegisterAttached(
            "MoveCommand", typeof(ICommand), typeof(ScheduleGridBehavior), new PropertyMetadata(null));

    /// <summary>Команда изменения длительности (<see cref="ScheduleTileResize"/>).</summary>
    public static readonly DependencyProperty ResizeCommandProperty =
        DependencyProperty.RegisterAttached(
            "ResizeCommand", typeof(ICommand), typeof(ScheduleGridBehavior), new PropertyMetadata(null));

    /// <summary>Команда одиночного клика по плитке — открыть Хаб предмета.</summary>
    public static readonly DependencyProperty TileClickCommandProperty =
        DependencyProperty.RegisterAttached(
            "TileClickCommand", typeof(ICommand), typeof(ScheduleGridBehavior), new PropertyMetadata(null));

    /// <summary>Число получасовых строк — чтобы перевести пиксели в строку.</summary>
    public static readonly DependencyProperty RowCountProperty =
        DependencyProperty.RegisterAttached(
            "RowCount", typeof(int), typeof(ScheduleGridBehavior), new PropertyMetadata(0));

    /// <summary>Помечает плитку: на ней включается перетаскивание и изменение длительности.</summary>
    public static readonly DependencyProperty IsTileProperty =
        DependencyProperty.RegisterAttached(
            "IsTile", typeof(bool), typeof(ScheduleGridBehavior), new PropertyMetadata(false, OnIsTileChanged));

    public static ICommand? GetEmptyCellCommand(DependencyObject e) =>
        (ICommand?)e.GetValue(EmptyCellCommandProperty);

    public static void SetEmptyCellCommand(DependencyObject e, ICommand? v) =>
        e.SetValue(EmptyCellCommandProperty, v);

    public static ICommand? GetMoveCommand(DependencyObject e) => (ICommand?)e.GetValue(MoveCommandProperty);

    public static void SetMoveCommand(DependencyObject e, ICommand? v) => e.SetValue(MoveCommandProperty, v);

    public static ICommand? GetResizeCommand(DependencyObject e) =>
        (ICommand?)e.GetValue(ResizeCommandProperty);

    public static void SetResizeCommand(DependencyObject e, ICommand? v) =>
        e.SetValue(ResizeCommandProperty, v);

    public static ICommand? GetTileClickCommand(DependencyObject e) =>
        (ICommand?)e.GetValue(TileClickCommandProperty);

    public static void SetTileClickCommand(DependencyObject e, ICommand? v) =>
        e.SetValue(TileClickCommandProperty, v);

    public static int GetRowCount(DependencyObject e) => (int)e.GetValue(RowCountProperty);

    public static void SetRowCount(DependencyObject e, int v) => e.SetValue(RowCountProperty, v);

    public static bool GetIsTile(DependencyObject e) => (bool)e.GetValue(IsTileProperty);

    public static void SetIsTile(DependencyObject e, bool v) => e.SetValue(IsTileProperty, v);

    // ---- пустое место -----------------------------------------------------------------------

    private static void OnEmptyCellCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.MouseLeftButtonUp -= OnCanvasClick;
        if (e.NewValue is not null)
        {
            element.MouseLeftButtonUp += OnCanvasClick;
        }
    }

    private static void OnCanvasClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element
            || GetEmptyCellCommand(element) is not { } command)
        {
            return;
        }

        var rows = GetRowCount(element);
        if (rows <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        var point = e.GetPosition(element);
        var day = Math.Clamp((int)(point.X / (element.ActualWidth / 7)), 0, 6);
        var row = Math.Clamp((int)(point.Y / (element.ActualHeight / rows)), 0, rows - 1);

        var hit = new ScheduleCellHit(day, row);
        if (command.CanExecute(hit))
        {
            command.Execute(hit);
        }
    }

    // ---- плитки: перенос и длительность -----------------------------------------------------

    private static void OnIsTileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement tile)
        {
            return;
        }

        tile.PreviewMouseLeftButtonDown -= OnTileDown;
        tile.PreviewMouseMove -= OnTileMove;
        tile.PreviewMouseLeftButtonUp -= OnTileUp;
        tile.MouseLeave -= OnTileLeave;

        if (e.NewValue is true)
        {
            tile.PreviewMouseLeftButtonDown += OnTileDown;
            tile.PreviewMouseMove += OnTileMove;
            tile.PreviewMouseLeftButtonUp += OnTileUp;
            tile.MouseLeave += OnTileLeave;
        }
    }

    private static DragState? _drag;

    private static void OnTileDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement tile || FindCanvas(tile) is not { } canvas)
        {
            return;
        }

        var inTile = e.GetPosition(tile);
        var resizing = inTile.Y >= tile.ActualHeight - ResizeGripHeight;

        _drag = new DragState(tile, canvas, e.GetPosition(canvas), resizing, Started: false);
    }

    private static void OnTileMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement tile)
        {
            return;
        }

        // Курсор-подсказка у нижнего края даже без нажатой кнопки — иначе ручку не найти.
        if (_drag is null && e.LeftButton != MouseButtonState.Pressed)
        {
            var y = e.GetPosition(tile).Y;
            tile.Cursor = y >= tile.ActualHeight - ResizeGripHeight ? Cursors.SizeNS : Cursors.Hand;
            return;
        }

        if (_drag is not { } state || !ReferenceEquals(state.Tile, tile))
        {
            return;
        }

        var current = e.GetPosition(state.Canvas);
        if (!state.Started)
        {
            var moved = Math.Abs(current.X - state.Origin.X) >= SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(current.Y - state.Origin.Y) >= SystemParameters.MinimumVerticalDragDistance;
            if (!moved)
            {
                return;
            }

            _drag = state with { Started = true };
            tile.CaptureMouse();
        }
    }

    private static void OnTileUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement tile || _drag is not { } state
            || !ReferenceEquals(state.Tile, tile))
        {
            _drag = null;
            return;
        }

        _drag = null;
        tile.ReleaseMouseCapture();

        var canvas = state.Canvas;
        var rows = GetRowCount(canvas);
        if (rows <= 0 || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0)
        {
            return;
        }

        var rowHeight = canvas.ActualHeight / rows;
        var dayWidth = canvas.ActualWidth / 7;

        // Порог перетаскивания не пройден — это обычный клик, ведём в Хаб предмета.
        if (!state.Started)
        {
            Invoke(GetTileClickCommand(canvas), tile.DataContext);
            e.Handled = true;
            return;
        }

        var point = e.GetPosition(canvas);

        if (state.Resizing)
        {
            var top = Grid.GetRow(tile) * rowHeight;
            var span = Math.Max(1, (int)Math.Round((point.Y - top) / rowHeight));
            Invoke(GetResizeCommand(canvas), new ScheduleTileResize(tile.DataContext, span));
        }
        else
        {
            // Тащим за точку, за которую взяли, — иначе плитка «прыгает» под курсор верхним краем.
            var offsetY = state.Origin.Y - (Grid.GetRow(tile) * rowHeight);
            var day = Math.Clamp((int)(point.X / dayWidth), 0, 6);
            var row = Math.Clamp((int)Math.Round((point.Y - offsetY) / rowHeight), 0, rows - 1);
            Invoke(GetMoveCommand(canvas), new ScheduleTileMove(tile.DataContext, day, row));
        }

        e.Handled = true;
    }

    private static void OnTileLeave(object sender, MouseEventArgs e)
    {
        if (_drag is { Started: false })
        {
            _drag = null;
        }
    }

    private static void Invoke(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    /// <summary>Полотно — ближайший вверх по дереву <see cref="Grid"/> с заданным числом строк.</summary>
    private static FrameworkElement? FindCanvas(FrameworkElement tile)
    {
        for (var current = tile.Parent as FrameworkElement; current is not null;
             current = current.Parent as FrameworkElement)
        {
            if (GetRowCount(current) > 0)
            {
                return current;
            }
        }

        return null;
    }

    private readonly record struct DragState(
        FrameworkElement Tile, FrameworkElement Canvas, Point Origin, bool Resizing, bool Started);
}
