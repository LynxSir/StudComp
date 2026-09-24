namespace StudComp.Core.Domain;

/// <summary>
/// Результат подгонки прямой <c>y = Intercept + Slope·x</c> методом наименьших квадратов.
/// <see cref="RSquared"/> — доля дисперсии <c>y</c>, объяснённая прямой, в диапазоне <c>[0, 1]</c>
/// (0 — прямая не лучше горизонтали, 1 — точки лежат на прямой).
/// </summary>
public readonly record struct LinearFit(double Slope, double Intercept, double RSquared)
{
    /// <summary>Значение подогнанной прямой в точке <paramref name="x"/>.</summary>
    public double PredictAt(double x) => Intercept + (Slope * x);
}

/// <summary>
/// Взвешенная линейная регрессия (WLS) — оценка тренда «стал сдавать хуже/лучше» по истории оценок
/// (ARCHITECTURE §9.4). Чистая детерминированная арифметика, только BCL — живёт в <c>Core</c>, а не
/// в модуле Органайзера (тот же довод, что у <see cref="WeekParityCalculator"/>): её используют и
/// стратегия прогноза, и график тренда, и тестируется она без ссылки на модуль.
/// </summary>
public static class LinearRegression
{
    /// <summary>Ниже этого значения знаменатель считаем нулевым — точек мало или они вырождены.</summary>
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Подгоняет прямую по точкам <paramref name="x"/>/<paramref name="y"/>, минимизируя
    /// <c>Σ wᵢ·(yᵢ − (a + b·xᵢ))²</c>. <paramref name="weights"/> = <see langword="null"/> — обычный МНК
    /// (все веса равны 1). Списки должны быть одной длины.
    /// </summary>
    /// <remarks>
    /// Вырожденные случаи не бросают исключение, а возвращают осмысленный «плоский» результат:
    /// пустой вход → <c>(0, 0, 0)</c>; одна точка или совпадающие <c>x</c> → наклон 0,
    /// свободный член = средневзвешенное <c>y</c>, <see cref="LinearFit.RSquared"/> = 0;
    /// постоянный <c>y</c> → <see cref="LinearFit.RSquared"/> = 0 (для оценки уверенности «нет разброса»
    /// трактуем как «тренда нет»).
    /// </remarks>
    public static LinearFit Fit(
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        IReadOnlyList<double>? weights = null)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count)
        {
            throw new ArgumentException("Длины x и y должны совпадать.", nameof(y));
        }

        if (weights is not null && weights.Count != x.Count)
        {
            throw new ArgumentException("Длина weights должна совпадать с x.", nameof(weights));
        }

        var n = x.Count;
        if (n == 0)
        {
            return new LinearFit(0d, 0d, 0d);
        }

        double sw = 0d, swx = 0d, swy = 0d, swxx = 0d, swxy = 0d, swyy = 0d;
        for (var i = 0; i < n; i++)
        {
            var w = weights is null ? 1d : Math.Max(0d, weights[i]);
            var xi = x[i];
            var yi = y[i];

            sw += w;
            swx += w * xi;
            swy += w * yi;
            swxx += w * xi * xi;
            swxy += w * xi * yi;
            swyy += w * yi * yi;
        }

        if (sw <= Epsilon)
        {
            return new LinearFit(0d, 0d, 0d);
        }

        var meanY = swy / sw;
        var denomX = (sw * swxx) - (swx * swx); // Sw·Var(x) — разброс по x
        var denomY = (sw * swyy) - (swy * swy); // Sw·Var(y) — разброс по y

        if (denomX <= Epsilon)
        {
            // Все x практически равны — наклон не определён, честно берём горизонталь на среднем.
            return new LinearFit(0d, meanY, 0d);
        }

        var covXy = (sw * swxy) - (swx * swy);
        var slope = covXy / denomX;
        var intercept = (swy - (slope * swx)) / sw;

        var rSquared = denomY <= Epsilon
            ? 0d
            : Math.Clamp((covXy * covXy) / (denomX * denomY), 0d, 1d);

        return new LinearFit(slope, intercept, rSquared);
    }
}
