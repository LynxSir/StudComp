using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Domain;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Табличные тесты чистой арифметики прогноза (ARCHITECTURE §9.4, §12 — «без моков, только данные in/out»).
/// Доля 0,7 по истории → итог мапится в шкалу: 5-балльная 4,1; 100-балльная 70; произвольная — по её границам.
/// </summary>
public sealed class WeightedAverageForecastStrategyTests
{
    private static readonly WeightedAverageForecastStrategy Sut = new();

    private static GradeEntry Graded(double raw, double max, double weight) => new()
    {
        Type = GradeEntryType.Attestation,
        IsPlanned = false,
        RawScore = (decimal)raw,
        MaxScore = (decimal)max,
        Weight = (decimal)weight,
        Date = new DateTime(2026, 3, 1),
    };

    private static PendingAssessment Pending(double max, double weight, int day = 15) =>
        new("Аттестация", (decimal)max, (decimal)weight, new DateTime(2026, 4, day));

    private static SubjectGradeHistory History(
        IReadOnlyList<GradeEntry> graded,
        IReadOnlyList<PendingAssessment> pending) =>
        new(Guid.NewGuid(), graded, pending);

    [Theory]
    [InlineData(GradeScaleKind.FivePoint, 4.10)]   // 2 + 0,7·3
    [InlineData(GradeScaleKind.HundredPoint, 70.0)] // 0 + 0,7·100
    public void Predicts_final_by_mapping_average_fraction_into_the_scale(GradeScaleKind kind, double expected)
    {
        var history = History(
            [Graded(80, 100, 1), Graded(60, 100, 1)],
            [Pending(100, 1)]);

        var result = Sut.Forecast(history, GradeScale.For(kind));

        Assert.Equal((decimal)expected, result.PredictedFinalScore, 2);
        Assert.Equal("weighted-average", result.StrategyUsed);
    }

    [Fact]
    public void Predicts_final_for_a_custom_scale()
    {
        var history = History([Graded(80, 100, 1), Graded(60, 100, 1)], [Pending(100, 1)]);

        var result = Sut.Forecast(history, new GradeScale(0m, 20m, 12m, GradeScaleKind.Custom));

        Assert.Equal(14m, result.PredictedFinalScore, 2); // 0,7·20
    }

    [Fact]
    public void Reverse_task_returns_the_score_needed_on_the_nearest_assessment()
    {
        var history = History([Graded(80, 100, 1), Graded(60, 100, 1)], [Pending(100, 1)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint), targetFinalGrade: 4.1m);

        Assert.True(result.IsAchievable);
        Assert.Equal(70m, result.RequiredScoreOnNextAssessment!.Value, 2); // 0,7·100
    }

    [Fact]
    public void Reverse_task_reports_zero_when_the_target_is_already_secured()
    {
        var history = History([Graded(90, 100, 3)], [Pending(100, 1)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint), targetFinalGrade: 3.0m);

        Assert.True(result.IsAchievable);
        Assert.Equal(0m, result.RequiredScoreOnNextAssessment!.Value);
    }

    [Fact]
    public void Reverse_task_reports_unreachable_when_even_a_perfect_finish_falls_short()
    {
        var history = History([Graded(20, 100, 3)], [Pending(100, 1)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint), targetFinalGrade: 5.0m);

        Assert.False(result.IsAchievable);
        Assert.Equal(100m, result.RequiredScoreOnNextAssessment!.Value); // кламп к MaxScore ближайшей
    }

    [Theory]
    [InlineData(4.0, true)]  // прогноз 4,1 — цель взята
    [InlineData(4.5, false)] // сдавать больше нечего, 4,1 < 4,5
    public void With_no_pending_assessments_there_is_nothing_to_require(double target, bool achievable)
    {
        var history = History([Graded(70, 100, 1)], []);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint), (decimal)target);

        Assert.Null(result.RequiredScoreOnNextAssessment);
        Assert.Equal(achievable, result.IsAchievable);
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
    public void All_zero_weights_do_not_throw()
    {
        var result = Sut.Forecast(
            History([Graded(80, 100, 0)], [Pending(100, 0)]),
            GradeScale.For(GradeScaleKind.FivePoint));

        Assert.Equal(ConfidenceLevel.Low, result.Confidence);
        Assert.Equal(2m, result.PredictedFinalScore);
    }

    [Theory]
    [InlineData(1, 4, ConfidenceLevel.Low)]     // доля 0,20
    [InlineData(1, 3, ConfidenceLevel.Medium)]  // доля 0,25 — граница
    [InlineData(3, 7, ConfidenceLevel.Medium)]  // доля 0,30
    [InlineData(6, 4, ConfidenceLevel.High)]    // доля 0,60 — граница
    public void Confidence_tracks_the_share_of_graded_weight(
        double gradedWeight,
        double pendingWeight,
        ConfidenceLevel expected)
    {
        var history = History([Graded(50, 100, gradedWeight)], [Pending(100, pendingWeight)]);

        var result = Sut.Forecast(history, GradeScale.For(GradeScaleKind.FivePoint));

        Assert.Equal(expected, result.Confidence);
    }

    [Fact]
    public void Predicted_and_required_scores_stay_within_bounds_across_a_sweep()
    {
        var scale = GradeScale.For(GradeScaleKind.HundredPoint);

        for (var i = 0; i < 40; i++)
        {
            var history = History(
                [Graded((i * 7) % 100, 100, 1 + (i % 3)), Graded((i * 13) % 100, 100, 2)],
                [Pending(50, 1 + (i % 2), day: 10 + i % 5), Pending(100, 2)]);

            var result = Sut.Forecast(history, scale, targetFinalGrade: 40m + i);

            Assert.InRange(result.PredictedFinalScore, scale.Min, scale.Max);
            if (result.RequiredScoreOnNextAssessment is { } required)
            {
                Assert.InRange(required, 0m, 50m); // ближайшая по дате — Pending(50, …)
            }
        }
    }
}
