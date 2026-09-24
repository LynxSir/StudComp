using System.Windows.Media;

namespace StudComp.ViewModels.Organizer;

/// <summary>Разбор <c>Subject.ColorHex</c> в кисти для акцентов карточек/плиток Органайзера.</summary>
internal static class SubjectColor
{
    private static readonly Color FallbackColor = Color.FromRgb(0x8A, 0x1C, 0x2B);

    private static readonly SolidColorBrush Fallback = Freeze(FallbackColor);

    private static readonly SolidColorBrush FallbackTint = Freeze(Tint(FallbackColor));

    public static Brush BrushFor(string? hex) =>
        TryParse(hex) is { } color ? Freeze(color) : Fallback;

    /// <summary>
    /// Сильно разбавленный цвет предмета — заливка плитки. Насыщенный цвет остаётся тонкой полосой
    /// слева: §19.2 требует держать акцент на 5–10 % площади, а не заливать им экран.
    /// </summary>
    public static Brush TintFor(string? hex) =>
        TryParse(hex) is { } color ? Freeze(Tint(color)) : FallbackTint;

    private static Color? TryParse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            return ColorConverter.ConvertFromString(hex) is Color color ? color : null;
        }
        catch (FormatException)
        {
            // Некорректный hex в данных — тихо уходим на дефолтный винный.
            return null;
        }
    }

    /// <summary>Подмешивает цвет к белому, оставляя ~14 % насыщенности.</summary>
    private static Color Tint(Color color) => Color.FromArgb(
        0xFF,
        (byte)(color.R + ((255 - color.R) * 0.86)),
        (byte)(color.G + ((255 - color.G) * 0.86)),
        (byte)(color.B + ((255 - color.B) * 0.86)));

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
