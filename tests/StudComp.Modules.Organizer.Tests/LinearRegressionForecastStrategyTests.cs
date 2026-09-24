using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Domain;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Табличные тесты стратегии прогноза с учётом тренда (ARCHITECTURE §9.4, §12 — «без моков, только
/// данные in/out»). Ключевое отличие от средневзвешенной: растущая история тянет прогноз вверх,
/// падающая — вниз; на ровной истории обе стратегии совпадают. Обратная задача — общий солвер,
/// поэтому её ответы совпадают с <see cref="WeightedAverageForecastStrategyTests"/>.
/// </summary>
public sealed class LinearRegressionForecastStrategyTests
{
    private static readonly LinearRegressionForecastStrategy Sut = new();
    private static readonly WeightedAverageForecastStrategy Weighted = new();
    private static readonly DateTime Origin = new(2026, 3, 1);

    private static GradeEntry Graded(double raw, double max, double weight, int day) => new()
    {
        Type = GradeEntryType.Attestation,
        IsPlanned = false,
        RawScore = (decimal)raw,
        MaxScore = (decimal)max,
        Weight = (decimal)weight,
        Date = Origin.AddDays(day),
    };

    private static PendingAssessment Pending(double max, double weight, int day) =>
        new("Аттестация", (decimal)max, (decimal)weight, Origin.AddDays(day));

    private static SubjectGradeHistory History(
        IReadOnlyList<GradeEntry> graded,
        IReadOnlyList<PendingAssessment> pending) =>
        new(Guid.NewGuid(), graded, pending);

    /// <summary>Четыре оценённые точки с равным шагом по времени, доли задаются вызывающим.</summary>
    private static SubjectGradeHistory FourPointHistory(
        double[] fractions, double gradedWeightEach, double pendingWeight, int pendingDay = 40) =>
        History(
            [.. fractions.Select((f, i) => Graded(f * 100, 100, gradedWeightEach, i * 10))],
            [Pending(100, pendingWeight, pendingDay)]);

    [Fact]
    public void Rising_history_predicts_higher_than_the_weighted_average()
    {
        var history = FourPointHistory([0.5, 0.6, 0.7, 0.8], gradedWeightEach: 1, pendingWeight: 1);
        var scale = GradeScale.For(GradeScaleKind.FivePoint);

        var linear = Sut.Forecast(history, scale);
        var weighted = Weighted.Forecast(history, scale);

        Assert.Equal("linear-regression", linear.StrategyUsed);
        Assert.Equal(4.1m, linear.PredictedFinalScore, 2);  // тренд продлевает 0,8 → 0,9 на день 40
        Assert.Equal(3.95m, weighted.PredictedFinalScore, 2);
        Assert.True(linear.PredictedFinalScore > weighted.PredictedFinalScore);
    }

    [Theory]
    [InlineData(GradeScaleKind.HundredPoint, 70.0)]
    public void Rising_history_maps_the_trend_fraction_into_the_scale(GradeScaleKind kind, double expected)
    {
        var history = FourPointHistory([0.5, 0.6, 0.7, 0.8], gradedWeightEach: 1, pendingWeight: 1);

        var result = Sut.Forecast(history, GradeScale.For(kind));

        Assert.Equal((decimal)expected, result.PredictedFinalScore, 2);
    }

    [Fact]
    public void Rising_history_maps_the_trend_fraction_into_a_custom_scale()
    {
        var history = FourPointHistory([0.5, 0.6, 0.7, 0.8], gradedWeightEach: 1, pendingWeight: 1);

        var result = Sut.Forecast(history, new GradeScale(0m, 20m, 12m, GradeScaleKind.Custom));

        Assert.Equal(14m, result.PredictedFinalScore, 2); // 0,7 · 20
    }

    [Fact]
    public void Declining_history_predicts_lower_than_the_weighted_average()
    {
        var history = FourPointHistory([0.9, 0.8, 0.7, 0.6], gradedWeightEach: 1, pendingWeight: 1);
        var scale = GradeScale.For(GradeScaleKind.FivePoint);

        var linear = Sut.Forecast(history, scale);
        var weighted = Weighted.Forecast(history, scale);

        Assert.Equal(4.1m, linear.PredictedFinalScore, 2);   // тренд продлевает 0,6 → 0,5
        Assert.Equal(4.25m, weighted.PredictedFinalScore, 2);
        Assert.True(linear.PredictedFinalScore < weighted.PredictedFinalScore);
    }

    [Fact]
    public void Flat_history_matches_the_weighted_average()
    {
        var history = FourPointHistory([0.7, 0.7, 0.7, 0.7], gradedWeightEach: 1, pendingWeight: 1);
        var scale = GradeScale.For(GradeScaleKind.FivePoint);

        var linear = Sut.Forecast(history, scale);
        var weighted = Weighted.Forecast(history, scale);

        Assert.Equal(weighted.PredictedFinalScore, linear.PredictedFinalScore, 2);
    }

    [Fact]
    public void Empty_history_predicts_the_scale_minimum_with_low_confidence()
    {
        var scale = GradeScale.For(GradeScaleKind.FivePoint);

        var result = Sut.Forecast(History([], []), scale, targetFinalGrade: 3.0m);

        Assert.Equal(scale.Min, result.PredictedFinalScore);
        Assert.Equal(ConfidenceLevel.Low, result.Confidence);
        Assert.Null(result.RequiredScoreOnNextAssessment);
        Assert.False(result.IsAchievable);
    }

    [Fact]
    public void A_single_graded_row_never_yields_more_than_low_confidence()
    {
        var history = History([Graded(80, 100, 10, 0)], [Pending(100, 1, 10)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint));

        Assert.Equal(ConfidenceLevel.Low, result.Confidence);
    }

    [Fact]
    public void Two_or_three_graded_rows_cap_confidence_at_medium()
    {
        // Доля веса оценённого 0,9 (была бы High), но регрессия по трём точкам — не выше «средней».
        var history = History(
            [Graded(50, 100, 3, 0), Graded(60, 100, 3, 10), Graded(70, 100, 3, 20)],
            [Pending(100, 1, 30)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint));

        Assert.Equal(ConfidenceLevel.Medium, result.Confidence);
    }

    [Fact]
    public void A_weak_fit_downgrades_confidence_below_high()
    {
        // Пять точек (счётчик не ограничивает), доля веса оценённого высокая, но разброс огромный → низкий R².
        var history = History(
            [
                Graded(20, 100, 2, 0), Graded(95, 100, 2, 10), Graded(15, 100, 2, 20),
                Graded(90, 100, 2, 30), Graded(20, 100, 2, 40),
            ],
            [Pending(100, 1, 50)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint));

        Assert.True(result.Confidence < ConfidenceLevel.High);
    }

    [Theory]
    [InlineData(1.0, 16.0, ConfidenceLevel.Low)]     // доля веса оценённого 0,20
    [InlineData(1.5, 14.0, ConfidenceLevel.Medium)]  // доля 0,30
    [InlineData(3.25, 7.0, ConfidenceLevel.High)]    // доля 0,65, 4 точки, идеальная прямая
    public void Confidence_follows_the_history_share_when_the_fit_is_clean(
        double gradedWeightEach, double pendingWeight, ConfidenceLevel expected)
    {
        var history = FourPointHistory([0.5, 0.6, 0.7, 0.8], gradedWeightEach, pendingWeight);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint));

        Assert.Equal(expected, result.Confidence);
    }

    [Fact]
    public void All_zero_weights_do_not_throw()
    {
        var result = Sut.Forecast(
            History([Graded(80, 100, 0, 0)], [Pending(100, 0, 10)]),
            GradeScale.For(GradeScaleKind.FivePoint));

        Assert.Equal(ConfidenceLevel.Low, result.Confidence);
        Assert.Equal(2m, result.PredictedFinalScore);
    }

    [Fact]
    public void Reverse_task_returns_the_score_needed_on_the_nearest_assessment()
    {
        var history = History([Graded(80, 100, 1, 0), Graded(60, 100, 1, 10)], [Pending(100, 1, 20)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint), targetFinalGrade: 4.1m);

        Assert.True(result.IsAchievable);
        Assert.Equal(70m, result.RequiredScoreOnNextAssessment!.Value, 2);
    }

    [Fact]
    public void Reverse_task_reports_zero_when_the_target_is_already_secured()
    {
        var history = History([Graded(90, 100, 3, 0)], [Pending(100, 1, 10)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint), targetFinalGrade: 3.0m);

        Assert.True(result.IsAchievable);
        Assert.Equal(0m, result.RequiredScoreOnNextAssessment!.Value);
    }

    [Fact]
    public void Reverse_task_reports_unreachable_when_even_a_perfect_finish_falls_short()
    {
        var history = History([Graded(20, 100, 3, 0)], [Pending(100, 1, 10)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint), targetFinalGrade: 5.0m);

        Assert.False(result.IsAchievable);
        Assert.Equal(100m, result.RequiredScoreOnNextAssessment!.Value);
    }

    [Theory]
    [InlineData(4.0, true)]
    [InlineData(4.5, false)]
    public void With_no_pending_assessments_there_is_nothing_to_require(double target, bool achievable)
    {
        var history = History([Graded(70, 100, 1, 0)], []);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint), (decimal)target);

        Assert.Null(result.RequiredScoreOnNextAssessment);
        Assert.Equal(achievable, result.IsAchievable);
    }

    [Fact]
    public void Predicted_and_required_scores_stay_within_bounds_across_a_sweep()
    {
        var scale = GradeScale.For(GradeScaleKind.HundredPoint);

        for (var i = 0; i < 40; i++)
        {
            var history = History(
                [
                    Graded((i * 7) % 100, 100, 1 + (i % 3), 0),
                    Graded((i * 13) % 100, 100, 2, 12),
                    Graded((i * 5) % 100, 100, 1, 25),
                ],
                [Pending(50, 1 + (i % 2), 30 + (i % 5)), Pending(100, 2, 45)]);

            var result = Sut.Forecast(history, scale, targetFinalGrade: 40m + i);

            Assert.InRange(result.PredictedFinalScore, scale.Min, scale.Max);
            if (result.RequiredScoreOnNextAssessment is { } required)
            {
                Assert.InRange(required, 0m, 50m); // ближайшая по дате — Pending(50, …)
            }
        }
    }
}
