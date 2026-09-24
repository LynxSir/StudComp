using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StudComp.Resources.Converters;

/// <summary>
/// Строка <c>#RRGGBB</c>/<c>#AARRGGBB</c> → <see cref="SolidColorBrush"/> для биндингов (напр.
/// <c>Subject.ColorHex</c> на <c>Background</c>). Некорректное значение → прозрачная кисть.
/// </summary>
public sealed class StringToBrushConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static StringToBrushConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex) is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
            }
            catch (FormatException)
            {
                // ниже — прозрачная кисть
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
