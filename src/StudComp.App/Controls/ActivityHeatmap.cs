using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace StudComp.Controls;

/// <summary>
/// Данные календаря активности: диапазон дней и число ответов в каждом.
/// </summary>
public sealed record ActivityHeatmapData(DateOnly From, DateOnly To, IReadOnlyDictionary<DateOnly, int> Counts)
{
    /// <summary>Самый нагруженный день — по нему нормируется интенсивность цвета.</summary>
    public int Peak { get; } = Counts.Count == 0 ? 0 : Counts.Values.Max();
}

/// <summary>
/// Календарь активности за год: сетка «неделя × день недели», интенсивность цветом (new_addons.md §6.5).
/// </summary>
/// <remarks>
/// Свой <see cref="FrameworkElement"/> с <see cref="OnRender"/> — charting-пакетов в проекте нет и не
/// будет, прецедент собственной отрисовки — <see cref="GradeTrendChart"/> (Phase 11). Кисти берутся
/// из темы через <see cref="FrameworkElement.TryFindResource"/>, поэтому смена темы подхватывается.
/// </remarks>
public sealed class ActivityHeatmap : FrameworkElement
{
    /// <summary>Сторона клетки одного дня.</summary>
    private const double CellSize = 11;

    /// <summary>Зазор между клетками.</summary>
    private const double CellGap = 3;

    /// <summary>Высота полосы с подписями месяцев.</summary>
    private const double MonthBandHeight = 16;

    /// <summary>Ширина колонки с подписями дней недели.</summary>
    private const double DayBandWidth = 26;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(ActivityHeatmapData),
        typeof(ActivityHeatmap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Что рисовать.</summary>
    public ActivityHeatmapData? Data
    {
        get => (ActivityHeatmapData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Data is null)
        {
            return new Size(0, 0);
        }

        var weeks = WeekCount(Data);
        var width = DayBandWidth + (weeks * (CellSize + CellGap));
        var height = MonthBandHeight + (7 * (CellSize + CellGap));

        return new Size(
            double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            height);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Data is not { } data)
        {
            return;
        }

        var accent = Resolve("ColorPrimaryBrush", Color.FromRgb(0x8A, 0x1C, 0x2B));
        var empty = Resolve("ControlFillColorSecondaryBrush", Color.FromRgb(0xEC, 0xEC, 0xEC));
        var caption = Resolve("TextFillColorTertiaryBrush", Color.FromRgb(0x90, 0x90, 0x90));

        var typeface = new Typeface(
            new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // Сетка выравнивается по понедельникам — так же, как расписание считает учебные недели.
        var start = data.From.AddDays(-DayIndex(data.From));
        var lastMonth = -1;

        for (var week = 0; week < WeekCount(data); week++)
        {
            var x = DayBandWidth + (week * (CellSize + CellGap));

            for (var day = 0; day < 7; day++)
            {
                var date = start.AddDays((week * 7) + day);
                if (date < data.From || date > data.To)
                {
                    continue;
                }

                var count = data.Counts.GetValueOrDefault(date);
                var y = MonthBandHeight + (day * (CellSize + CellGap));

                var brush = count == 0
                    ? empty
                    : new SolidColorBrush(Blend(accent.Color, Intensity(count, data.Peak)));

                drawingContext.DrawRoundedRectangle(brush, null, new Rect(x, y, CellSize, CellSize), 2, 2);

                if (day == 0 && date.Month != lastMonth)
                {
                    lastMonth = date.Month;
                    DrawText(drawingContext, MonthName(date.Month), typeface, caption, x, 1);
                }
            }
        }

        // Подписи дней недели — только по понедельникам, средам и пятницам, иначе колонка рябит.
        foreach (var (index, title) in (ReadOnlySpan<(int, string)>)[(0, "пн"), (2, "ср"), (4, "пт")])
        {
            DrawText(
                drawingContext,
                title,
                typeface,
                caption,
                0,
                MonthBandHeight + (index * (CellSize + CellGap)) - 1);
        }
    }

    private void DrawText(
        DrawingContext context,
        string text,
        Typeface typeface,
        SolidColorBrush brush,
        double x,
        double y)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            10,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        context.DrawText(formatted, new Point(x, y));
    }

    private SolidColorBrush Resolve(string key, Color fallback) =>
        TryFindResource(key) as SolidColorBrush ?? new SolidColorBrush(fallback);

    /// <summary>Интенсивность в четыре ступени — плавный градиент читается хуже, чем ступени.</summary>
    private static double Intensity(int count, int peak)
    {
        if (peak <= 0)
        {
            return 1;
        }

        var share = (double)count / peak;

        return share switch
        {
            <= 0.25 => 0.35,
            <= 0.5 => 0.55,
            <= 0.75 => 0.78,
            _ => 1.0,
        };
    }

    private static Color Blend(Color color, double intensity) => Color.FromArgb(
        0xFF,
        (byte)(color.R + ((255 - color.R) * (1 - intensity))),
        (byte)(color.G + ((255 - color.G) * (1 - intensity))),
        (byte)(color.B + ((255 - color.B) * (1 - intensity))));

    private static int WeekCount(ActivityHeatmapData data)
    {
        var start = data.From.AddDays(-DayIndex(data.From));
        return (int)Math.Ceiling((data.To.DayNumber - start.DayNumber + 1) / 7.0);
    }

    /// <summary>Понедельник — нулевой день недели: у нас учебная неделя, а не американская.</summary>
    private static int DayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    private static string MonthName(int month) => month switch
    {
        1 => "янв", 2 => "фев", 3 => "мар", 4 => "апр", 5 => "май", 6 => "июн",
        7 => "июл", 8 => "авг", 9 => "сен", 10 => "окт", 11 => "ноя", _ => "дек",
    };
}
