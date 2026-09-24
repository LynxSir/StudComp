using System.Globalization;
using System.Windows.Media;
using StudComp.ViewModels.Settings;

namespace StudComp.Services;

/// <summary>
/// Разбор строки акцента из настроек (<c>AppearanceOptions.Accent</c>): ключ пресета или
/// <c>#RRGGBB</c>. Общий код для <see cref="ThemeService"/> и <see cref="MotionService"/>.
/// </summary>
internal static class AccentPalette
{
    /// <summary>Винный по умолчанию (ARCHITECTURE §19.2).</summary>
    public static readonly Color Wine = Color.FromRgb(0x8A, 0x1C, 0x2B);

    /// <summary>Цвет по строке акцента. Непонятное значение → винный.</summary>
    public static Color Resolve(string? accent)
    {
        if (string.IsNullOrWhiteSpace(accent))
        {
            return Wine;
        }

        var value = accent.Trim();

        foreach (var preset in SettingsChoices.AccentPresets)
        {
            if (string.Equals(preset.Key, value, StringComparison.OrdinalIgnoreCase))
            {
                return Parse(preset.Hex) ?? Wine;
            }
        }

        return Parse(value) ?? Wine;
    }

    /// <summary>Осветлить цвет на долю <paramref name="amount"/> (0..1) в сторону белого.</summary>
    public static Color Lighten(Color color, double amount) => Mix(color, Colors.White, amount);

    /// <summary>Затемнить цвет на долю <paramref name="amount"/> (0..1) в сторону чёрного.</summary>
    public static Color Darken(Color color, double amount) => Mix(color, Colors.Black, amount);

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    private static Color? Parse(string hex)
    {
        var text = hex.StartsWith('#') ? hex[1..] : hex;
        if (text.Length == 6 &&
            int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        }

        return null;
    }
}
