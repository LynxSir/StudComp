using System.Text.Json;
using System.Text.Json.Serialization;

namespace StudComp.Core.Domain;

/// <summary>Тип элемента рисунка заметки (new_addons.md §12).</summary>
public enum NoteDrawingElementKind
{
    /// <summary>Произвольный штрих от руки (перо) или его кусок, оставшийся после ластика.</summary>
    Stroke = 0,

    /// <summary>Прямая линия между двумя точками.</summary>
    Line = 1,

    /// <summary>Прямоугольник по двум противоположным углам.</summary>
    Rectangle = 2,

    /// <summary>Эллипс, вписанный в прямоугольник по двум противоположным углам.</summary>
    Ellipse = 3,

    /// <summary>Стрелка: отрезок с наконечником на конце.</summary>
    Arrow = 4,

    /// <summary>Текстовая подпись в точке.</summary>
    Text = 5,
}

/// <summary>Точка холста рисунка. Отдельный тип, а не <c>System.Windows.Point</c> — модель не знает про WPF.</summary>
public readonly record struct NoteDrawingPoint(double X, double Y);

/// <summary>
/// Один элемент рисунка — штрих, фигура или текст. Один и тот же тип для всех видов: и во время
/// рисования, и при пересборке визуала из сохранённой модели используется одна дорожка.
/// </summary>
/// <param name="Kind">Вид элемента.</param>
/// <param name="Points">
/// Опорные точки: у штриха — все точки штриха по порядку; у линии/прямоугольника/эллипса/стрелки — две
/// точки (начало и конец/противоположные углы); у текста — одна точка (левый верхний угол).
/// </param>
/// <param name="ColorHex">Цвет в форме <c>#RRGGBB</c>.</param>
/// <param name="Thickness">Толщина линии в пикселях экрана (не используется для текста).</param>
/// <param name="Text">Содержимое текстового элемента; <see langword="null"/> для остальных видов.</param>
/// <param name="FontSize">Кегль текстового элемента.</param>
public sealed record NoteDrawingElement(
    NoteDrawingElementKind Kind,
    IReadOnlyList<NoteDrawingPoint> Points,
    string ColorHex,
    double Thickness,
    string? Text = null,
    double FontSize = 16);

/// <summary>Рисунок целиком: размер холста и упорядоченный список элементов.</summary>
public sealed class NoteDrawingDocument
{
    public double CanvasWidth { get; set; } = 900;

    public double CanvasHeight { get; set; } = 560;

    public List<NoteDrawingElement> Elements { get; set; } = [];

    private const string SchemaTag = "rubrica.note-drawing";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Сериализовать в версионированный конверт (по образцу <c>RulesJson</c>/<c>CardsJson</c>).</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(
            new NoteDrawingEnvelope
            {
                Schema = SchemaTag,
                Version = CurrentVersion,
                CanvasWidth = CanvasWidth,
                CanvasHeight = CanvasHeight,
                Elements = Elements,
            },
            Options);

    /// <summary>
    /// Разобрать сайдкар-файл рисунка. Вход — файл на диске, который мог оказаться битым, чужим или
    /// созданным другой версией программы: разбор никогда не бросает, при любой проблеме — <see
    /// langword="false"/> и <paramref name="document"/> = <see langword="null"/>.
    /// </summary>
    public static bool TryParse(string? json, out NoteDrawingDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        NoteDrawingEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<NoteDrawingEnvelope>(json, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (envelope is null || !string.Equals(envelope.Schema, SchemaTag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        document = new NoteDrawingDocument
        {
            CanvasWidth = envelope.CanvasWidth > 0 ? envelope.CanvasWidth : 900,
            CanvasHeight = envelope.CanvasHeight > 0 ? envelope.CanvasHeight : 560,
            Elements = envelope.Elements ?? [],
        };
        return true;
    }

    private sealed class NoteDrawingEnvelope
    {
        public string Schema { get; set; } = string.Empty;

        public int Version { get; set; }

        public double CanvasWidth { get; set; }

        public double CanvasHeight { get; set; }

        public List<NoteDrawingElement>? Elements { get; set; }
    }
}

/// <summary>
/// Геометрия наконечника стрелки — общая формула для живого предпросмотра во время рисования и для
/// пересборки визуала из сохранённой модели, чтобы не разъезжаться в двух копиях (new_addons.md §12).
/// </summary>
public static class NoteDrawingGeometry
{
    /// <summary>Угол раскрытия наконечника от оси стрелки, в радианах (30°).</summary>
    private const double WingAngle = Math.PI / 6;

    /// <summary>
    /// Две точки наконечника стрелки, направленной из <paramref name="start"/> в <paramref name="end"/>.
    /// Вырожденный случай (совпадающие точки) — наконечник вырождается в точку конца, без исключения.
    /// </summary>
    public static (NoteDrawingPoint Wing1, NoteDrawingPoint Wing2) ArrowHead(
        NoteDrawingPoint start, NoteDrawingPoint end, double size)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < double.Epsilon)
        {
            return (end, end);
        }

        var angle = Math.Atan2(dy, dx);
        var wing1Angle = angle + Math.PI - WingAngle;
        var wing2Angle = angle + Math.PI + WingAngle;

        return (
            new NoteDrawingPoint(end.X + (size * Math.Cos(wing1Angle)), end.Y + (size * Math.Sin(wing1Angle))),
            new NoteDrawingPoint(end.X + (size * Math.Cos(wing2Angle)), end.Y + (size * Math.Sin(wing2Angle))));
    }
}
