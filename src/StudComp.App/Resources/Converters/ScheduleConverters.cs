using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StudComp.Resources.Converters;

/// <summary>
/// Отступы плитки внутри колонки дня по её дорожке: при «обеих неделях» числитель и знаменатель
/// делят ширину пополам и встают рядом (new_addons.md §5).
/// </summary>
/// <remarks>
/// Отступ в процентах ширины колонки посчитать в разметке нечем, а ширина зависит от размера окна —
/// поэтому <see cref="IMultiValueConverter"/> поверх фактической ширины полотна.
/// </remarks>
public sealed class LaneMarginConverter : IMultiValueConverter
{
    public static readonly LaneMarginConverter Instance = new();

    /// <summary>Зазор между плитками, px.</summary>
    private const double Gap = 2;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3
            || values[0] is not int lane
            || values[1] is not int laneCount
            || values[2] is not double canvasWidth
            || laneCount <= 1
            || canvasWidth <= 0)
        {
            return new Thickness(Gap, 1, Gap, 1);
        }

        var dayWidth = canvasWidth / 7;
        var laneWidth = dayWidth / laneCount;

        return new Thickness(
            Gap + (lane * laneWidth),
            1,
            Gap + ((laneCount - lane - 1) * laneWidth),
            1);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Сравнение двух значений на равенство — подсветка выбранного пункта в сегментированном
/// переключателе, где <c>IsChecked</c> должен зависеть от текущего выбора во ViewModel.
/// </summary>
public sealed class EqualityConverter : IMultiValueConverter
{
    public static readonly EqualityConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Позиция линии «сейчас» по горизонтали: сдвигает её к колонке сегодняшнего дня.
/// </summary>
public sealed class TodayColumnOffsetConverter : IMultiValueConverter
{
    public static readonly TodayColumnOffsetConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3
            || values[0] is not int column
            || values[1] is not double canvasWidth
            || values[2] is not double top
            || column < 0
            || canvasWidth <= 0)
        {
            return new Thickness(0);
        }

        var dayWidth = canvasWidth / 7;
        return new Thickness(column * dayWidth, top, (6 - column) * dayWidth, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
