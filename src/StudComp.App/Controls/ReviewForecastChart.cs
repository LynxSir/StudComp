using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace StudComp.Controls;

/// <summary>Данные кривой нагрузки: с какого дня, на сколько дней вперёд и сколько карточек в каждом.</summary>
public sealed record ReviewForecastData(DateOnly From, int Days, IReadOnlyDictionary<DateOnly, int> Counts)
{
    /// <summary>Самый нагруженный день — по нему нормируется высота столбцов.</summary>
    public int Peak { get; } = Counts.Count == 0 ? 0 : Counts.Values.Max();
}

/// <summary>
/// Кривая нагрузки вперёд: сколько карточек придёт на повторение в ближайшие дни (new_addons.md §6.5).
/// Помогает заранее увидеть завал и не устроить его себе перед сессией.
/// </summary>
/// <remarks>
/// Как и <see cref="ActivityHeatmap"/>, это собственный <see cref="FrameworkElement"/> с
/// <see cref="OnRender"/>: charting-пакетов в проекте нет, а прецедент — <see cref="GradeTrendChart"/>.
/// </remarks>
public sealed class ReviewForecastChart : FrameworkElement
{
    /// <summary>Высота полосы с подписями дат под столбцами.</summary>
    private const double LabelBandHeight = 16;

    /// <summary>Зазор между столбцами.</summary>
    private const double BarGap = 3;

    /// <summary>Минимальная высота столбца, чтобы ненулевой день был виден.</summary>
    private const double MinBarHeight = 2;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(ReviewForecastData),
        typeof(ReviewForecastChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Что рисовать.</summary>
    public ReviewForecastData? Data
    {
        get => (ReviewForecastData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Data is not { Days: > 0 } data || ActualWidth <= 0 || ActualHeight <= LabelBandHeight)
        {
            return;
        }

        var accent = Resolve("ColorPrimaryBrush", Color.FromRgb(0x8A, 0x1C, 0x2B));
        var muted = Resolve("ControlFillColorSecondaryBrush", Color.FromRgb(0xEC, 0xEC, 0xEC));
        var caption = Resolve("TextFillColorTertiaryBrush", Color.FromRgb(0x90, 0x90, 0x90));

        var typeface = new Typeface(
            new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var plotHeight = ActualHeight - LabelBandHeight;
        var barWidth = Math.Max(2, ((ActualWidth + BarGap) / data.Days) - BarGap);
        var peak = Math.Max(1, data.Peak);

        // Направляющая «ноль» — иначе пустые дни выглядят как обрыв, а не как отсутствие нагрузки.
        drawingContext.DrawLine(
            new Pen(muted, 1),
            new Point(0, plotHeight + 0.5),
            new Point(ActualWidth, plotHeight + 0.5));

        for (var i = 0; i < data.Days; i++)
        {
            var day = data.From.AddDays(i);
            var count = data.Counts.GetValueOrDefault(day);
            var x = i * (barWidth + BarGap);

            if (count > 0)
            {
                var height = Math.Max(MinBarHeight, plotHeight * count / peak);
                drawingContext.DrawRoundedRectangle(
                    accent,
                    null,
                    new Rect(x, plotHeight - height, barWidth, height),
                    2,
                    2);
            }

            // Подписи через каждые пять дней — иначе ось превращается в кашу.
            if (i % 5 == 0)
            {
                var formatted = new FormattedText(
                    day.ToString("dd.MM", CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    10,
                    caption,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                drawingContext.DrawText(formatted, new Point(x, plotHeight + 2));
            }
        }
    }

    private SolidColorBrush Resolve(string key, Color fallback) =>
        TryFindResource(key) as SolidColorBrush ?? new SolidColorBrush(fallback);
}
