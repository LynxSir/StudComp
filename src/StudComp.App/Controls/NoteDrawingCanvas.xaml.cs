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

    private bool _eraserGesture;

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

        // Потеря захвата мыши оставляла фигуру предпросмотра висеть и тянуться за курсором без
        // нажатой кнопки.
        Overlay.LostMouseCapture += (_, _) => CancelPreviewShape();

        Ink.PreviewMouseLeftButtonDown += OnInkMouseDown;
        Ink.PreviewMouseLeftButtonUp += OnInkMouseUp;
        Ink.LostMouseCapture += (_, _) => EndEraserGesture();

        // Штатная stylus-обвязка WPF добавляет заметную задержку ввода даже без дигитайзера.
        Stylus.SetIsFlicksEnabled(Ink, false);
        Stylus.SetIsTapFeedbackEnabled(Ink, false);
        Stylus.SetIsTouchFeedbackEnabled(Ink, false);
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
        // Идёт ввод текста — Ctrl+Z отменяет именно его, а не предыдущее действие. Прежний порядок
        // (сначала зафиксировать текст, потом откатить) съедал ровно этот только что созданный шаг.
        if (CancelPendingText())
        {
            return;
        }

        _history.Undo();
        RebuildAll();
    }

    public void Redo()
    {
        if (CancelPendingText())
        {
            return;
        }

        _history.Redo();
        RebuildAll();
    }

    public void Clear()
    {
        CancelPendingText();
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
        // Рендерится внутренняя сетка, а не Paper: у того есть темовая рамка в 1 пиксель, которая
        // попадала в картинку, а сам рисунок на её толщину обрезался.
        RootGrid.UpdateLayout();

        var width = (int)Math.Ceiling(Math.Max(1, RootGrid.ActualWidth));
        var height = (int)Math.Ceiling(Math.Max(1, RootGrid.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(RootGrid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Кегль текстового инструмента.</summary>
    public static readonly DependencyProperty TextFontSizeProperty = DependencyProperty.Register(
        nameof(TextFontSize), typeof(double), typeof(NoteDrawingCanvas), new PropertyMetadata(18d));

    public double TextFontSize
    {
        get => (double)GetValue(TextFontSizeProperty);
        set => SetValue(TextFontSizeProperty, value);
    }

    /// <summary>
    /// Замороженные кисти по цвету. Незамороженная <see cref="SolidColorBrush"/> тащит за собой
    /// оповещения об изменении и не может быть разделена с потоком композиции, а полная пересборка
    /// рисунка создавала их заново на каждый элемент.
    /// </summary>
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = [];

    private static SolidColorBrush FrozenBrush(string? colorHex)
    {
        var key = colorHex ?? string.Empty;
        if (BrushCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush(ParseColor(key));
        brush.Freeze();
        BrushCache[key] = brush;
        return brush;
    }

    /// <summary>
    /// Ластик удаляет штрихи <b>во время</b> протяжки, и каждое удаление поднимало полную
    /// пересборку модели и событие <see cref="Changed"/>. Жест схлопывается в одну запись истории:
    /// именно это и делало ластик самым медленным инструментом.
    /// </summary>
    private void OnInkMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Tool == NoteDrawingTool.Eraser)
        {
            _eraserGesture = true;
        }
    }

    private void OnInkMouseUp(object sender, MouseButtonEventArgs e) => EndEraserGesture();

    private void EndEraserGesture()
    {
        if (!_eraserGesture)
        {
            return;
        }

        _eraserGesture = false;
        SyncStrokesToHistory();
    }

    /// <summary>Привести модель в соответствие с текущим набором штрихов и сообщить об изменении.</summary>
    private void SyncStrokesToHistory()
    {
        var strokes = Ink.Strokes.Select(ToElement).ToList();
        var shapes = _history.Elements.Where(el => el.Kind != NoteDrawingElementKind.Stroke).ToList();
        _history.Push([.. strokes, .. shapes]);
        RaiseChanged();
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

        // Подгонка кривой делает статичную отрисовку дороже динамической и даёт видимый рывок в
        // момент отпускания кнопки — «как в Paint» её быть не должно.
        FitToCurve = false,
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
        if (_suppressStrokesChanged || _eraserGesture)
        {
            return;
        }

        SyncStrokesToHistory();
    }

    private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Tool == NoteDrawingTool.Text)
        {
            BeginTextInput(e.GetPosition(Overlay));
            e.Handled = true;
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

        // Состояние снимается ДО освобождения захвата: освобождение синхронно поднимает
        // LostMouseCapture, и обработчик отмены иначе унёс бы только что дорисованную фигуру.
        var end = e.GetPosition(Overlay);
        var preview = _previewShape;
        _dragStart = null;
        _previewShape = null;
        Overlay.ReleaseMouseCapture();

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

            case ShapePath { Data: PathGeometry geometry }:
                UpdateArrowGeometry(geometry, start, current, StrokeThicknessValue);
                break;
        }
    }

    private Line NewLine() => new()
    {
        Stroke = FrozenBrush(StrokeColorHex),
        StrokeThickness = StrokeThicknessValue,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private Rectangle NewRectangle() => new()
    {
        Stroke = FrozenBrush(StrokeColorHex),
        StrokeThickness = StrokeThicknessValue,
        Fill = Brushes.Transparent,
    };

    private Ellipse NewEllipse() => new()
    {
        Stroke = FrozenBrush(StrokeColorHex),
        StrokeThickness = StrokeThicknessValue,
        Fill = Brushes.Transparent,
    };

    private ShapePath NewArrowPath() => new()
    {
        Stroke = FrozenBrush(StrokeColorHex),
        StrokeThickness = StrokeThicknessValue,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Data = NewArrowGeometry(),
    };

    /// <summary>Снять незавершённую фигуру: захват мыши потерян, рисовать больше нечего.</summary>
    private void CancelPreviewShape()
    {
        var preview = _previewShape;
        _previewShape = null;
        _dragStart = null;

        if (preview is not null)
        {
            Overlay.Children.Remove(preview);
        }
    }

    /// <summary>
    /// Заготовка стрелки: две фигуры, точки которых потом только переставляются. Прежний вариант
    /// собирал новую <c>GeometryGroup</c> с тремя отрезками на каждое движение мыши.
    /// </summary>
    private static PathGeometry NewArrowGeometry()
    {
        var shaft = new PathFigure { Segments = { new LineSegment() } };
        var head = new PathFigure { Segments = { new LineSegment(), new LineSegment() } };
        return new PathGeometry { Figures = { shaft, head } };
    }

    private static void UpdateArrowGeometry(PathGeometry geometry, Point start, Point end, double thickness)
    {
        var wingSize = Math.Max(10, thickness * 4);
        var (wing1, wing2) = NoteDrawingGeometry.ArrowHead(
            new NoteDrawingPoint(start.X, start.Y), new NoteDrawingPoint(end.X, end.Y), wingSize);

        var shaft = geometry.Figures[0];
        shaft.StartPoint = start;
        ((LineSegment)shaft.Segments[0]).Point = end;

        var head = geometry.Figures[1];
        head.StartPoint = new Point(wing1.X, wing1.Y);
        ((LineSegment)head.Segments[0]).Point = end;
        ((LineSegment)head.Segments[1]).Point = new Point(wing2.X, wing2.Y);
    }

    /// <summary>Готовая неизменяемая стрелка — для пересборки уже нарисованного.</summary>
    private static Geometry BuildArrowGeometry(Point start, Point end, double thickness)
    {
        var geometry = NewArrowGeometry();
        UpdateArrowGeometry(geometry, start, end, thickness);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Начать ввод текста. Событие мыши обязательно помечается обработанным, а фокус берётся
    /// отложенно: иначе клик продолжает всплывать, фокус перехватывает хост диалога, и поле
    /// мгновенно закрывается по <c>LostFocus</c> — со стороны выглядит как «нажимаю, и ничего не
    /// происходит» (new_addons.md §12, жалоба владельца).
    /// </summary>
    private void BeginTextInput(Point at)
    {
        CommitPendingText();

        var brush = FrozenBrush(StrokeColorHex);
        var textBox = new TextBox
        {
            MinWidth = 140,
            FontSize = TextFontSize,
            Foreground = brush,
            Background = Brushes.White,
            BorderBrush = brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
        };

        Canvas.SetLeft(textBox, at.X);
        Canvas.SetTop(textBox, at.Y);
        Overlay.Children.Add(textBox);
        _activeTextBox = textBox;

        // Подписка на потерю фокуса только после того, как фокус реально получен: пока поле его не
        // взяло, любая возня с фокусом при открытии не должна его убивать.
        textBox.GotKeyboardFocus += (_, _) => textBox.LostKeyboardFocus += (_, _) => CommitPendingText();

        textBox.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                CommitPendingText();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                // Помечаем обработанным, иначе Escape закроет весь диалог рисования.
                Overlay.Children.Remove(textBox);
                _activeTextBox = null;
                args.Handled = true;
            }
        };

        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            () =>
            {
                textBox.Focus();
                Keyboard.Focus(textBox);
            });
    }

    /// <summary>Выбросить незавершённый ввод текста. <see langword="true"/> — было что выбрасывать.</summary>
    private bool CancelPendingText()
    {
        var textBox = _activeTextBox;
        _activeTextBox = null;

        if (textBox is null || !Overlay.Children.Contains(textBox))
        {
            return false;
        }

        Overlay.Children.Remove(textBox);
        return true;
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
        var fontSize = textBox.FontSize;
        Overlay.Children.Remove(textBox);

        if (text.Length == 0)
        {
            return;
        }

        var element = new NoteDrawingElement(
            NoteDrawingElementKind.Text,
            [new NoteDrawingPoint(left, top)],
            StrokeColorHex,
            0,
            text,
            fontSize);

        _history.Push([.. _history.Elements, element]);

        // Через полную пересборку, а не добавлением визуала напрямую: иначе порядок элементов на
        // экране разошёлся бы с моделью до ближайшей отмены.
        RebuildAll();
    }

    private static TextBlock BuildTextVisual(string text, string colorHex, double fontSize, double left, double top)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = FrozenBrush(colorHex),
        };
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        return block;
    }

    private void RebuildAll()
    {
        CancelPendingText();
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
                    Stroke = FrozenBrush(element.ColorHex),
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
                    Stroke = FrozenBrush(element.ColorHex),
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
                    Stroke = FrozenBrush(element.ColorHex),
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
        Stroke = FrozenBrush(colorHex),
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
                FitToCurve = false,
            },
        };
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
