using System.Globalization;
using System.Windows.Data;
using StudComp.Resources;

namespace StudComp.Resources.Converters;

/// <summary>
/// <see cref="DateTimeOffset"/> дедлайна → «Сегодня, 18:00» / «Завтра» / «через 3 дня» /
/// «просрочено N дней назад» (Phase 13.5) — тонкая обёртка над <see cref="RelativeDayFormatter"/> для
/// биндинга в <c>ItemsControl.ItemTemplate</c>, где вызвать статический метод напрямую нельзя.
/// </summary>
public sealed class RelativeDayToTextConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static RelativeDayToTextConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset due ? RelativeDayFormatter.Format(due) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
