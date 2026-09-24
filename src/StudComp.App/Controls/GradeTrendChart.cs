using System.Globalization;
using System.Windows;
using System.Windows.Media;
using StudComp.Core.Domain;

namespace StudComp.Controls;

/// <summary>Одна точка графика тренда: день от первой оценки и доля от максимума (0..1).</summary>
public readonly record struct TrendPoint(double TimeDays, double Fraction, bool IsPass);

/// <summary>
/// Данные графика тренда оценок — уже в «долях» и днях; перевод в пиксели делает сам контрол.
/// </summary>
public sealed record GradeTrendData(
    IReadOnlyList<TrendPoint> Graded,
    IReadOnlyList<TrendPoint> Pending,
    LinearFit Regression,
    double WeightedMeanFraction,
    double PredictedFraction,
    double PassFraction,
    string FirstDateLabel,
    string LastDateLabel,
    string PredictedLabel);

/// <summary>
/// График тренда оценок по предмету (Phase 11, ARCHITECTURE §9.4): точки оценок по времени, линия
/// тренда (линейная регрессия, сплошная по диапазону оценок + пунктир-экстраполяция) и горизонтальный
/// пунктир средневзвешенной — это и есть визуальная «разница стратегий». Рисуется вручную в
/// <see cref="OnRender"/>: без charting-пакета, чётко масштабируется и подхватывает смену темы.
/// </summary>
public sealed class GradeTrendChart : FrameworkElement
{
    /// <summary>Данные для отрисовки; <see langword="null"/> — контрол пуст.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(GradeTrendData),
        typeof(GradeTrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    public GradeTrendData? Data
    {
        get => (GradeTrendData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var data = Data;
        var width = ActualWidth;
        var height = ActualHeight;
        if (data is null || width <= 40d || height <= 40d)
        {
            return;
        }

        // Палитра из темы — перечитывается каждый рендер, поэтому смена темы подхватывается сама.
        var muted = ResolveBrush("TextFillColorSecondaryBrush", Color.FromRgb(0x88, 0x88, 0x88));
        var accent = ResolveBrush("AccentTextFillColorPrimaryBrush", Color.FromRgb(0x8A, 0x1C, 0x2B));
        var passBrush = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
        var failBrush = Frozen(Color.FromRgb(0xC0, 0x37, 0x2A));
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        const double padLeft = 34d;
        const double padRight = 10d;
        const double padTop = 10d;
        const double padBottom = 20d;
        var plotW = width - padLeft - padRight;
        var plotH = height - padTop - padBottom;

        var allX = data.Graded.Select(p => p.TimeDays)
            .Concat(data.Pending.Select(p => p.TimeDays))
            .DefaultIfEmpty(0d)
            .ToList();
        var maxX = Math.Max(1d, allX.Max());

        double PxX(double days) => padLeft + (plotW * Math.Clamp(days / maxX, 0d, 1d));
        double PxY(double fraction) => padTop + (plotH * (1d - Math.Clamp(fraction, 0d, 1d)));

        void Label(string text, double x, double y, Brush brush, double size, bool rightAlign = false)
        {
            var ft = new FormattedText(
                text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                LabelTypeface, size, brush, pixelsPerDip);
            dc.DrawText(ft, new Point(rightAlign ? x - ft.Width : x, y));
        }

        // --- Горизонтальные направляющие: 0 / порог сдачи / 100 % ---
        var gridPen = new Pen(muted, 0.5d) { DashStyle = DashStyles.Dash };
        gridPen.Freeze();
        foreach (var (frac, caption) in new[] { (0d, "0%"), (data.PassFraction, "порог"), (1d, "100%") })
        {
            var y = PxY(frac);
            dc.DrawLine(gridPen, new Point(padLeft, y), new Point(width - padRight, y));
            Label(caption, padLeft - 4d, y - 7d, muted, 9d, rightAlign: true);
        }

        // --- Фактическая траектория оценок (тонкая линия) ---
        if (data.Graded.Count >= 2)
        {
            var trajPen = new Pen(muted, 1d);
            trajPen.Freeze();
            for (var i = 1; i < data.Graded.Count; i++)
            {
                dc.DrawLine(
                    trajPen,
                    new Point(PxX(data.Graded[i - 1].TimeDays), PxY(data.Graded[i - 1].Fraction)),
                    new Point(PxX(data.Graded[i].TimeDays), PxY(data.Graded[i].Fraction)));
            }
        }

        // --- Средневзвешенная (горизонталь, пунктир) ---
        var wmPen = new Pen(muted, 1.5d) { DashStyle = new DashStyle(new double[] { 3d, 3d }, 0d) };
        wmPen.Freeze();
        var wmY = PxY(data.WeightedMeanFraction);
        dc.DrawLine(wmPen, new Point(padLeft, wmY), new Point(width - padRight, wmY));

        // --- Линия тренда: сплошная по диапазону оценок, пунктир — экстраполяция дальше ---
        var lastGradedX = data.Graded.Count > 0 ? data.Graded[^1].TimeDays : 0d;
        var solidEndX = Math.Clamp(lastGradedX, 0d, maxX);
        var trendPen = new Pen(accent, 2d);
        trendPen.Freeze();
        dc.DrawLine(
            trendPen,
            new Point(PxX(0d), PxY(data.Regression.PredictAt(0d))),
            new Point(PxX(solidEndX), PxY(data.Regression.PredictAt(solidEndX))));
        if (maxX > solidEndX + 0.5d)
        {
            var extPen = new Pen(accent, 2d) { DashStyle = new DashStyle(new double[] { 2d, 2d }, 0d) };
            extPen.Freeze();
            dc.DrawLine(
                extPen,
                new Point(PxX(solidEndX), PxY(data.Regression.PredictAt(solidEndX))),
                new Point(PxX(maxX), PxY(data.Regression.PredictAt(maxX))));
        }

        // --- Прогноз итога (горизонталь, точечный) ---
        var predPen = new Pen(accent, 1d) { DashStyle = new DashStyle(new double[] { 1d, 2d }, 0d) };
        predPen.Freeze();
        var predY = PxY(data.PredictedFraction);
        dc.DrawLine(predPen, new Point(padLeft, predY), new Point(width - padRight, predY));
        Label(data.PredictedLabel, width - padRight, Math.Max(padTop, predY - 13d), accent, 10d, rightAlign: true);

        // --- Точки оценок (заливка по «сдал/не сдал») ---
        foreach (var p in data.Graded)
        {
            dc.DrawEllipse(p.IsPass ? passBrush : failBrush, null, new Point(PxX(p.TimeDays), PxY(p.Fraction)), 3.5d, 3.5d);
        }

        // --- Полые маркеры запланированных аттестаций на линии тренда ---
        var pendPen = new Pen(accent, 1.5d);
        pendPen.Freeze();
        foreach (var p in data.Pending)
        {
            var y = PxY(Math.Clamp(data.Regression.PredictAt(p.TimeDays), 0d, 1d));
            dc.DrawEllipse(Brushes.Transparent, pendPen, new Point(PxX(p.TimeDays), y), 3.5d, 3.5d);
        }

        // --- Подписи крайних дат ---
        Label(data.FirstDateLabel, padLeft, height - padBottom + 4d, muted, 9d);
        Label(data.LastDateLabel, width - padRight, height - padBottom + 4d, muted, 9d, rightAlign: true);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private Brush ResolveBrush(string resourceKey, Color fallback) =>
        TryFindResource(resourceKey) as SolidColorBrush ?? Frozen(fallback);
}
