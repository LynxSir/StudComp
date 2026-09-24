using StudComp.Core.Abstractions.Organizer;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Прямые тесты общего солвера обратной задачи (ARCHITECTURE §9.4). Держит его поведение зафиксированным,
/// чтобы стратегии прогноза не разъехались в ответе «сколько нужно на ближайшей аттестации».
/// </summary>
public sealed class ReverseTaskSolverTests
{
    private static readonly DateTime Origin = new(2026, 3, 1);
    private static readonly GradeScale Five = GradeScale.For(GradeScaleKind.FivePoint);

    private static SubjectGradeHistory WithPending(params (double Max, int Day)[] pending) =>
        new(
            Guid.NewGuid(),
            [],
            [.. pending.Select(p => new PendingAssessment("Аттестация", (decimal)p.Max, 1m, Origin.AddDays(p.Day)))]);

    [Fact]
    public void Expresses_the_required_average_as_a_score_on_the_nearest_assessment()
    {
        var (required, achievable) = ReverseTaskSolver.Solve(
            WithPending((Max: 100, Day: 20)),
            Five, earned: 1.4m, pendingWeight: 1m, totalWeight: 3m, predictedFinal: 4.1m, target: 4.1m);

        Assert.True(achievable);
        Assert.Equal(70m, required!.Value, 2); // (4,1−2)/3 · 3 − 1,4 = 0,7 → 0,7 · 100
    }

    [Fact]
    public void Uses_the_earliest_pending_assessment_for_the_score()
    {
        var (required, _) = ReverseTaskSolver.Solve(
            WithPending((Max: 50, Day: 10), (Max: 100, Day: 30)),
            Five, earned: 0m, pendingWeight: 2m, totalWeight: 2m, predictedFinal: 2m, target: 5m);

        Assert.Equal(50m, required!.Value); // требуемая доля 1,0 → балл = max ближайшей (50)
    }

    [Fact]
    public void Reports_zero_when_the_target_is_already_secured()
    {
        var (required, achievable) = ReverseTaskSolver.Solve(
            WithPending((Max: 100, Day: 10)),
            Five, earned: 2.7m, pendingWeight: 1m, totalWeight: 4m, predictedFinal: 4.5m, target: 3.0m);

        Assert.True(achievable);
        Assert.Equal(0m, required!.Value);
    }

    [Fact]
    public void Reports_unreachable_and_clamps_to_the_nearest_maximum()
    {
        var (required, achievable) = ReverseTaskSolver.Solve(
            WithPending((Max: 100, Day: 10)),
            Five, earned: 0.6m, pendingWeight: 1m, totalWeight: 4m, predictedFinal: 2.5m, target: 5.0m);

        Assert.False(achievable);
        Assert.Equal(100m, required!.Value);
    }

    [Theory]
    [InlineData(4.0, true)]
    [InlineData(4.5, false)]
    public void Nothing_left_to_take_defers_to_the_forecast(double target, bool achievable)
    {
        var (required, isAchievable) = ReverseTaskSolver.Solve(
            new SubjectGradeHistory(Guid.NewGuid(), [], []),
            Five, earned: 0.7m, pendingWeight: 0m, totalWeight: 1m, predictedFinal: 4.1m, target: (decimal)target);

        Assert.Null(required);
        Assert.Equal(achievable, isAchievable);
    }

    [Fact]
    public void Degenerate_scale_defers_to_the_forecast()
    {
        var (required, achievable) = ReverseTaskSolver.Solve(
            WithPending((Max: 100, Day: 10)),
            new GradeScale(5m, 5m, 5m, GradeScaleKind.Custom),
            earned: 0m, pendingWeight: 1m, totalWeight: 1m, predictedFinal: 5m, target: 5m);

        Assert.Null(required);
        Assert.True(achievable);
    }
}
