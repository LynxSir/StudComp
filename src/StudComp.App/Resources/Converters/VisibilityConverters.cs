using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StudComp.Resources.Converters;

/// <summary><see langword="true"/> → <see cref="Visibility.Visible"/>, иначе <see cref="Visibility.Collapsed"/>.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static BooleanToVisibilityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Обратное к <see cref="BooleanToVisibilityConverter"/> — для подсказок «здесь пока пусто».</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static InverseBooleanToVisibilityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Инверсия <see cref="bool"/> — типовой случай «кнопка активна, пока не идёт работа».</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static InverseBooleanConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Значение не <see langword="null"/> → <see cref="Visibility.Visible"/>, иначе <see cref="Visibility.Collapsed"/>.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static NullToCollapsedConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary><see langword="null"/> → <see cref="Visibility.Visible"/> (показать пустое состояние), иначе <see cref="Visibility.Collapsed"/>.</summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static NullToVisibleConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Непустая строка → <see cref="Visibility.Visible"/>; пустая или пробелы → <see cref="Visibility.Collapsed"/>.</summary>
public sealed class StringPresenceToVisibilityConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static StringPresenceToVisibilityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
