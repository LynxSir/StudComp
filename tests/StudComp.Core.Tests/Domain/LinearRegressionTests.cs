using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты <see cref="LinearRegression"/> (ARCHITECTURE §9.4, §12 — «без моков, только данные in/out»).
/// Точки на прямой должны давать её точный наклон и <c>R² = 1</c>; вырожденный вход — «плоский» результат.
/// </summary>
public sealed class LinearRegressionTests
{
    [Fact]
    public void Exact_line_recovers_slope_intercept_and_unit_fit()
    {
        var fit = LinearRegression.Fit([0d, 1d, 2d, 3d], [1d, 3d, 5d, 7d]);

        Assert.Equal(2d, fit.Slope, 9);
        Assert.Equal(1d, fit.Intercept, 9);
        Assert.Equal(1d, fit.RSquared, 9);
    }

    [Fact]
    public void PredictAt_uses_the_fitted_line()
    {
        var fit = LinearRegression.Fit([0d, 1d, 2d], [1d, 3d, 5d]);

        Assert.Equal(11d, fit.PredictAt(5d), 9); // 1 + 2·5
    }

    [Fact]
    public void Descending_data_yields_a_negative_slope()
    {
        var fit = LinearRegression.Fit([0d, 1d, 2d, 3d, 4d], [0.9d, 0.8d, 0.65d, 0.5d, 0.35d]);

        Assert.True(fit.Slope < 0d);
        Assert.InRange(fit.RSquared, 0.9d, 1d);
    }

    [Fact]
    public void Noisy_but_rising_data_keeps_slope_sign_and_partial_fit()
    {
        var fit = LinearRegression.Fit([0d, 1d, 2d, 3d, 4d, 5d], [0.40d, 0.55d, 0.45d, 0.70d, 0.65d, 0.85d]);

        Assert.True(fit.Slope > 0d);
        Assert.InRange(fit.RSquared, 0.0d, 1.0d);
        Assert.True(fit.RSquared is > 0d and < 1d);
    }

    [Fact]
    public void Weights_pull_the_line_towards_the_heavy_points()
    {
        double[] x = [0d, 1d, 2d, 3d];
        double[] y = [0d, 0d, 1d, 1d];

        var even = LinearRegression.Fit(x, y);
        var heavyTail = LinearRegression.Fit(x, y, [1d, 1d, 8d, 8d]);

        // Тяжёлый «хвост» (y≈1) поднимает линию в точке x=3 ближе к 1.
        Assert.True(heavyTail.PredictAt(3d) > even.PredictAt(3d));
    }

    [Fact]
    public void Equal_x_values_fall_back_to_a_horizontal_line_at_the_weighted_mean()
    {
        var fit = LinearRegression.Fit([2d, 2d, 2d], [1d, 2d, 3d]);

        Assert.Equal(0d, fit.Slope, 9);
        Assert.Equal(2d, fit.Intercept, 9);
        Assert.Equal(0d, fit.RSquared, 9);
    }

    [Fact]
    public void Constant_y_reports_zero_slope_and_zero_fit()
    {
        var fit = LinearRegression.Fit([0d, 1d, 2d, 3d], [0.7d, 0.7d, 0.7d, 0.7d]);

        Assert.Equal(0d, fit.Slope, 9);
        Assert.Equal(0.7d, fit.Intercept, 9);
        Assert.Equal(0d, fit.RSquared, 9);
    }

    [Fact]
    public void Single_point_gives_a_flat_line_through_it()
    {
        var fit = LinearRegression.Fit([5d], [0.42d]);

        Assert.Equal(0d, fit.Slope, 9);
        Assert.Equal(0.42d, fit.Intercept, 9);
        Assert.Equal(0d, fit.RSquared, 9);
    }

    [Fact]
    public void Empty_input_returns_the_zero_fit()
    {
        var fit = LinearRegression.Fit([], []);

        Assert.Equal(new LinearFit(0d, 0d, 0d), fit);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Mismatched_lengths_throw(int yCount)
    {
        var y = Enumerable.Repeat(1d, yCount).ToArray();

        Assert.Throws<ArgumentException>(() => LinearRegression.Fit([0d, 1d, 2d, 3d], y));
    }

    [Fact]
    public void RSquared_stays_within_the_unit_interval_across_a_sweep()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var x = new double[6];
            var y = new double[6];
            for (var i = 0; i < 6; i++)
            {
                x[i] = i;
                y[i] = Math.Clamp((0.02d * seed * i) + (((seed * 7) + (i * 13)) % 11 / 30d), 0d, 1d);
            }

            var fit = LinearRegression.Fit(x, y);

            Assert.InRange(fit.RSquared, 0d, 1d);
        }
    }
}
