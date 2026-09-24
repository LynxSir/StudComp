using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using StudComp.Core.Domain;

namespace StudComp.Controls;

/// <summary>Инструмент рисования на холсте заметки (new_addons.md §12, Phase 13.7).</summary>
public enum NoteDrawingTool
{
    Pen,
    Eraser,
    Line,
    Rectangle,
    Ellipse,
    Arrow,
    Text,
}

/// <summary>
/// Холст рисования: перо/ластик — штатный <see cref="InkCanvas"/> (нижний слой, единственный источник
/// истины по штрихам), фигуры и текст — свой <see cref="Canvas"/> сверху, собранный мышью.
/// </summary>
/// <remarks>
/// Осознанное упрощение: штрихи всегда рисуются <b>ниже</b> всех фигур/текста независимо от реального
/// порядка рисования — перенос уже нарисованного штриха в общий с фигурами слой ради точного z-order
/// потребовал бы своего инструмента-«ластика» поверх геометрии вместо штатного
/// <see cref="InkCanvasEditingMode.EraseByStroke"/>, а это уже полноценный векторный редактор, а не
/// «мощный набор инструментов рисования», который был запрошен (new_addons.md, преамбула: «не
/// усложнять сразу без необходимости»). Нет и инструмента выбора/перемещения/изменения размера уже
/// нарисованного элемента — поправить фигуру можно только отменой и повтором.
/// </remarks>
public partial class NoteDrawingCanvas : UserControl
{
    private NoteDrawingHistory _history = new();
    private bool _suppressStrokesChanged;

    private Point? _dragStart;
    private Shape? _previewShape;
    private TextBox? _activeTextBox;

    public NoteDrawingCanvas()
    {
        InitializeComponent();

        Paper.Width = 900;
        Paper.Height = 560;

        Ink.DefaultDrawingAttributes = BuildDrawingAttributes();
        Ink.EditingMode = InkCanvasEditingMode.Ink;
        Ink.Strokes.StrokesChanged += OnStrokesChanged;

        Overlay.MouseLeftButtonDown += OnOverlayMouseDown;
        Overlay.MouseMove += OnOverlayMouseMove;
        Overlay.MouseLeftButtonUp += OnOverlayMouseUp;
    }

    /// <summary>Активный инструмент рисования.</summary>
    public static readonly DependencyProperty ToolProperty = DependencyProperty.Register(
        nameof(Tool), typeof(NoteDrawingTool), typeof(NoteDrawingCanvas),
        new PropertyMetadata(NoteDrawingTool.Pen, OnToolChanged));

    public NoteDrawingTool Tool
    {
        get => (NoteDrawingTool)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    /// <summary>Цвет пера/фигур/текста в форме <c>#RRGGBB</c>.</summary>
    public static readonly DependencyProperty StrokeColorHexProperty = DependencyProperty.Register(
        nameof(StrokeColorHex), typeof(string), typeof(NoteDrawingCanvas),
        new PropertyMetadata("#1A1A1A", OnDrawingAttributesChanged));

    public string StrokeColorHex
    {
        get => (string)GetValue(StrokeColorHexProperty);
        set => SetValue(StrokeColorHexProperty, value);
    }

    /// <summary>Толщина линии в пикселях.</summary>
    public static readonly DependencyProperty StrokeThicknessValueProperty = DependencyProperty.Register(
        nameof(StrokeThicknessValue), typeof(double), typeof(NoteDrawingCanvas),
        new PropertyMetadata(3d, OnDrawingAttributesChanged));

    public double StrokeThicknessValue
    {
        get => (double)GetValue(StrokeThicknessValueProperty);
        set => SetValue(StrokeThicknessValueProperty, value);
    }

    /// <summary>Есть хотя бы один элемент — от этого зависит доступность кнопки «Сохранить»/«Вставить».</summary>
    public bool HasElements => _history.Elements.Count > 0;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    /// <summary>Завершено очередное действие (штрих/фигура/текст/отмена/повтор/очистка).</summary>
    public event EventHandler? Changed;

    public void Undo()
    {
        CommitPendingText();
        _history.Undo();
        RebuildAll();
    }

    public void Redo()
    {
        CommitPendingText();
        _history.Redo();
        RebuildAll();
    }

    public void Clear()
    {
        CommitPendingText();
        _history.Push([]);
        RebuildAll();
    }

    /// <summary>Загрузить существующий рисунок для повторного редактирования (или очистить холст — <see langword="null"/>).</summary>
    public void Load(NoteDrawingDocument? document)
    {
        Paper.Width = document is { CanvasWidth: > 0 } ? document.CanvasWidth : 900;
        Paper.Height = document is { CanvasHeight: > 0 } ? document.CanvasHeight : 560;

        _history = new NoteDrawingHistory(document?.Elements ?? []);
        RebuildAll();
    }

    /// <summary>Текущий рисунок как переносимая модель (для JSON-сайдкара).</summary>
    public NoteDrawingDocument ExportDocument() => new()
    {
        CanvasWidth = Paper.Width,
        CanvasHeight = Paper.Height,
        Elements = [.. _history.Elements],
    };

    /// <summary>Снимок холста в PNG — то, что вставляется в Markdown.</summary>
    public byte[] RenderToPng()
    {
        var width = (int)Math.Ceiling(Paper.Width);
        var height = (int)Math.Ceiling(Paper.Height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(Paper);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void OnToolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NoteDrawingCanvas)d).ApplyTool();

    private static void OnDrawingAttributesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NoteDrawingCanvas)d).Ink.DefaultDrawingAttributes = ((NoteDrawingCanvas)d).BuildDrawingAttributes();

    private void ApplyTool()
    {
        CommitPendingText();

        Ink.EditingMode = Tool switch
        {
            NoteDrawingTool.Pen => InkCanvasEditingMode.Ink,
            NoteDrawingTool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None,
        };

        Overlay.IsHitTestVisible = Tool is NoteDrawingTool.Line or NoteDrawingTool.Rectangle
            or NoteDrawingTool.Ellipse or NoteDrawingTool.Arrow or NoteDrawingTool.Text;
    }

    private DrawingAttributes BuildDrawingAttributes() => new()
    {
        Color = ParseColor(StrokeColorHex),
        Width = Math.Max(1, StrokeThicknessValue),
        Height = Math.Max(1, StrokeThicknessValue),
        FitToCurve = true,
    };

    private static Color ParseColor(string hex)
    {
        try
        {
            return ColorConverter.ConvertFromString(hex) is Color color ? color : Colors.Black;
        }
        catch (FormatException)
        {
            return Colors.Black;
        }
    }

    /// <summary>Синхронизация модели после любого изменения штрихов — и рисования, и ластика разом.</summary>
    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_suppressStrokesChanged)
        {
            return;
        }

        var strokes = Ink.Strokes.Select(ToElement).ToList();
        var shapes = _history.Elements.Where(el => el.Kind != NoteDrawingElementKind.Stroke).ToList();
        _history.Push([.. strokes, .. shapes]);
        RaiseChanged();
    }

    private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Tool == NoteDrawingTool.Text)
        {
            BeginTextInput(e.GetPosition(Overlay));
            return;
        }

        CommitPendingText();

        _dragStart = e.GetPosition(Overlay);
        _previewShape = CreatePreviewShape();
        if (_previewShape is not null)
        {
            Overlay.Children.Add(_previewShape);
        }

        Overlay.CaptureMouse();
    }

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _previewShape is null)
        {
            return;
        }

        UpdatePreviewShape(_previewShape, start, e.GetPosition(Overlay));
    }

    private void OnOverlayMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        Overlay.ReleaseMouseCapture();
        var end = e.GetPosition(Overlay);
        _dragStart = null;

        var preview = _previewShape;
        _previewShape = null;

        if (preview is null)
        {
            return;
        }

        // Клик почти без протяжки — фигура без размера бесполезна, не засоряем рисунок.
        if (Math.Abs(end.X - start.X) < 2 && Math.Abs(end.Y - start.Y) < 2)
        {
            Overlay.Children.Remove(preview);
            return;
        }

        var kind = Tool switch
        {
            NoteDrawingTool.Line => NoteDrawingElementKind.Line,
            NoteDrawingTool.Rectangle => NoteDrawingElementKind.Rectangle,
            NoteDrawingTool.Ellipse => NoteDrawingElementKind.Ellipse,
            NoteDrawingTool.Arrow => NoteDrawingElementKind.Arrow,
            _ => NoteDrawingElementKind.Line,
        };

        var element = new NoteDrawingElement(
            kind,
            [new NoteDrawingPoint(start.X, start.Y), new NoteDrawingPoint(end.X, end.Y)],
            StrokeColorHex,
            StrokeThicknessValue);

        _history.Push([.. _history.Elements, element]);
        RaiseChanged();
    }

    private Shape? CreatePreviewShape() => Tool switch
    {
        NoteDrawingTool.Line => NewLine(),
        NoteDrawingTool.Rectangle => NewRectangle(),
        NoteDrawingTool.Ellipse => NewEllipse(),
        NoteDrawingTool.Arrow => NewArrowPath(),
        _ => null,
    };

    private void UpdatePreviewShape(Shape shape, Point start, Point current)
    {
        switch (shape)
        {
            case Line line:
                line.X1 = start.X;
                line.Y1 = start.Y;
                line.X2 = current.X;
                line.Y2 = current.Y;
                break;

            case Rectangle or Ellipse:
                var x = Math.Min(start.X, current.X);
                var y = Math.Min(start.Y, current.Y);
                shape.Width = Math.Abs(current.X - start.X);
                shape.Height = Math.Abs(current.Y - start.Y);
                Canvas.SetLeft(shape, x);
                Canvas.SetTop(shape, y);
                break;

            case ShapePath path:
                path.Data = BuildArrowGeometry(start, current, StrokeThicknessValue);
                break;
        }
    }

    private Line NewLine() => new()
    {
        Stroke = new SolidColorBrush(ParseColor(StrokeColorHex)),
        StrokeThickness = StrokeThicknessValue,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private Rectangle NewRectangle() => new()
    {
        Stroke = new SolidColorBrush(ParseColor(StrokeColorHex)),
        StrokeThickness = StrokeThicknessValue,
        Fill = Brushes.Transparent,
    };

    private Ellipse NewEllipse() => new()
    {
        Stroke = new SolidColorBrush(ParseColor(StrokeColorHex)),
        StrokeThickness = StrokeThicknessValue,
        Fill = Brushes.Transparent,
    };

    private ShapePath NewArrowPath() => new()
    {
        Stroke = new SolidColorBrush(ParseColor(StrokeColorHex)),
        StrokeThickness = StrokeThicknessValue,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static Geometry BuildArrowGeometry(Point start, Point end, double thickness)
    {
        var wingSize = Math.Max(10, thickness * 4);
        var (wing1, wing2) = NoteDrawingGeometry.ArrowHead(
            new NoteDrawingPoint(start.X, start.Y), new NoteDrawingPoint(end.X, end.Y), wingSize);

        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry(start, end));
        group.Children.Add(new LineGeometry(end, new Point(wing1.X, wing1.Y)));
        group.Children.Add(new LineGeometry(end, new Point(wing2.X, wing2.Y)));
        return group;
    }

    private void BeginTextInput(Point at)
    {
        CommitPendingText();

        var textBox = new TextBox
        {
            MinWidth = 120,
            FontSize = 18,
            Foreground = new SolidColorBrush(ParseColor(StrokeColorHex)),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(ParseColor(StrokeColorHex)),
            BorderThickness = new Thickness(1),
        };

        Canvas.SetLeft(textBox, at.X);
        Canvas.SetTop(textBox, at.Y);
        Overlay.Children.Add(textBox);
        _activeTextBox = textBox;

        textBox.LostFocus += (_, _) => CommitPendingText();
        textBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                CommitPendingText();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                Overlay.Children.Remove(textBox);
                _activeTextBox = null;
                args.Handled = true;
            }
        };

        textBox.Focus();
    }

    private void CommitPendingText()
    {
        var textBox = _activeTextBox;
        _activeTextBox = null;
        if (textBox is null || !Overlay.Children.Contains(textBox))
        {
            return;
        }

        var text = textBox.Text?.Trim() ?? string.Empty;
        var left = Canvas.GetLeft(textBox);
        var top = Canvas.GetTop(textBox);
        Overlay.Children.Remove(textBox);

        if (text.Length == 0)
        {
            return;
        }

        var colorHex = StrokeColorHex;
        var block = BuildTextVisual(text, colorHex, 18, left, top);
        Overlay.Children.Add(block);

        var element = new NoteDrawingElement(
            NoteDrawingElementKind.Text, [new NoteDrawingPoint(left, top)], colorHex, 0, text, 18);
        _history.Push([.. _history.Elements, element]);
        RaiseChanged();
    }

    private static TextBlock BuildTextVisual(string text, string colorHex, double fontSize, double left, double top)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(ParseColor(colorHex)),
        };
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        return block;
    }

    private void RebuildAll()
    {
        CommitPendingText();
        _suppressStrokesChanged = true;
        try
        {
            Ink.Strokes.Clear();
            foreach (var element in _history.Elements.Where(el => el.Kind == NoteDrawingElementKind.Stroke))
            {
                Ink.Strokes.Add(ToStroke(element));
            }

            Overlay.Children.Clear();
            foreach (var element in _history.Elements.Where(el => el.Kind != NoteDrawingElementKind.Stroke))
            {
                var visual = BuildVisual(element);
                if (visual is not null)
                {
                    Overlay.Children.Add(visual);
                }
            }
        }
        finally
        {
            _suppressStrokesChanged = false;
        }

        RaiseChanged();
    }

    private UIElement? BuildVisual(NoteDrawingElement element)
    {
        if (element.Points.Count < 2 && element.Kind != NoteDrawingElementKind.Text)
        {
            return null;
        }

        switch (element.Kind)
        {
            case NoteDrawingElementKind.Line:
            {
                var (a, b) = (element.Points[0], element.Points[1]);
                var line = NewLineWith(element.ColorHex, element.Thickness);
                line.X1 = a.X; line.Y1 = a.Y; line.X2 = b.X; line.Y2 = b.Y;
                return line;
            }

            case NoteDrawingElementKind.Rectangle:
            {
                var (a, b) = (element.Points[0], element.Points[1]);
                var rect = new Rectangle
                {
                    Stroke = new SolidColorBrush(ParseColor(element.ColorHex)),
                    StrokeThickness = element.Thickness,
                    Fill = Brushes.Transparent,
                    Width = Math.Abs(b.X - a.X),
                    Height = Math.Abs(b.Y - a.Y),
                };
                Canvas.SetLeft(rect, Math.Min(a.X, b.X));
                Canvas.SetTop(rect, Math.Min(a.Y, b.Y));
                return rect;
            }

            case NoteDrawingElementKind.Ellipse:
            {
                var (a, b) = (element.Points[0], element.Points[1]);
                var ellipse = new Ellipse
                {
                    Stroke = new SolidColorBrush(ParseColor(element.ColorHex)),
                    StrokeThickness = element.Thickness,
                    Fill = Brushes.Transparent,
                    Width = Math.Abs(b.X - a.X),
                    Height = Math.Abs(b.Y - a.Y),
                };
                Canvas.SetLeft(ellipse, Math.Min(a.X, b.X));
                Canvas.SetTop(ellipse, Math.Min(a.Y, b.Y));
                return ellipse;
            }

            case NoteDrawingElementKind.Arrow:
            {
                var (a, b) = (element.Points[0], element.Points[1]);
                return new ShapePath
                {
                    Stroke = new SolidColorBrush(ParseColor(element.ColorHex)),
                    StrokeThickness = element.Thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Data = BuildArrowGeometry(
                        new Point(a.X, a.Y), new Point(b.X, b.Y), element.Thickness),
                };
            }

            case NoteDrawingElementKind.Text:
            {
                var at = element.Points[0];
                return BuildTextVisual(
                    element.Text ?? string.Empty, element.ColorHex, element.FontSize, at.X, at.Y);
            }

            default:
                return null;
        }
    }

    private static Line NewLineWith(string colorHex, double thickness) => new()
    {
        Stroke = new SolidColorBrush(ParseColor(colorHex)),
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static NoteDrawingElement ToElement(Stroke stroke)
    {
        var points = stroke.StylusPoints.Select(p => new NoteDrawingPoint(p.X, p.Y)).ToList();
        var color = stroke.DrawingAttributes.Color;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        return new NoteDrawingElement(
            NoteDrawingElementKind.Stroke, points, hex, stroke.DrawingAttributes.Width);
    }

    private static Stroke ToStroke(NoteDrawingElement element)
    {
        var points = new StylusPointCollection(element.Points.Select(p => new Point(p.X, p.Y)));
        return new Stroke(points)
        {
            DrawingAttributes = new DrawingAttributes
            {
                Color = ParseColor(element.ColorHex),
                Width = Math.Max(1, element.Thickness),
                Height = Math.Max(1, element.Thickness),
                FitToCurve = true,
            },
        };
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
