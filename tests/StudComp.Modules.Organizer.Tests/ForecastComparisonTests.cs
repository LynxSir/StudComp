using StudComp.Core.Abstractions.Organizer;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Тесты фразы-объяснения разницы стратегий (DoD Phase 11 — «объяснимо простыми словами»).
/// </summary>
public sealed class ForecastComparisonTests
{
    private static readonly GradeScale Five = GradeScale.For(GradeScaleKind.FivePoint);

    private static ForecastResult Result(decimal predicted) =>
        new(predicted, null, ConfidenceLevel.Medium, "x");

    [Fact]
    public void Rising_grades_are_described_as_growing()
    {
        var text = ForecastComparison.Describe(Result(3.8m), Result(4.3m), Five, trendSlopePerDay: 0.01d);

        Assert.Contains("растут", text);
        Assert.Contains("3,8", text);
        Assert.Contains("4,3", text);
    }

    [Fact]
    public void Falling_grades_are_described_as_declining()
    {
        var text = ForecastComparison.Describe(Result(4.3m), Result(3.8m), Five, trendSlopePerDay: -0.01d);

        Assert.Contains("снижаются", text);
    }

    [Fact]
    public void Close_forecasts_are_described_as_holding_steady()
    {
        var text = ForecastComparison.Describe(Result(4.0m), Result(4.03m), Five, trendSlopePerDay: 0.0000001d);

        Assert.Contains("держатся ровно", text);
    }
}
