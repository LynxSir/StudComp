using StudComp.Core.Abstractions.Organizer;

namespace StudComp.Modules.Organizer.Services.ForecastStrategies;

/// <summary>
/// Обратная задача прогноза (ARCHITECTURE §9.4): «сколько нужно набрать на ближайшей аттестации,
/// чтобы выйти на целевую итоговую оценку». Ответ не зависит от того, как посчитан прямой прогноз —
/// это требуемая средняя доля по всем оставшимся аттестациям, выраженная баллом на ближайшей по дате.
/// Поэтому солвер общий для всех <see cref="IGradeForecastStrategy"/>: «что сдать» одинаково при любой
/// стратегии, различается только сам прогноз. При единственной планируемой аттестации сводится к формуле §9.4.
/// </summary>
internal static class ReverseTaskSolver
{
    /// <summary>Допуск на ошибку деления при сравнениях «уже достигнуто» / «недостижимо».</summary>
    private const decimal Epsilon = 0.0005m;

    /// <summary>
    /// Считает требуемый балл на ближайшей аттестации и признак достижимости цели.
    /// </summary>
    /// <param name="earned">Уже набранное: <c>Σ (доля_i · вес_i)</c> по оценённым строкам.</param>
    /// <param name="pendingWeight">Суммарный вес ещё не сданных аттестаций.</param>
    /// <param name="totalWeight">Суммарный вес всех аттестаций (оценённых и запланированных).</param>
    /// <param name="predictedFinal">Прямой прогноз итога — нужен, когда сдавать больше нечего.</param>
    /// <param name="target">Целевая итоговая оценка.</param>
    /// <returns>
    /// <c>RequiredOnNext</c> — балл на ближайшей аттестации (кламп к <c>[0, MaxScore]</c>), либо
    /// <see langword="null"/>, если сдавать больше нечего. <c>IsAchievable</c> — <see langword="false"/>,
    /// если даже максимум на всём оставшемся не выводит на цель.
    /// </returns>
    public static (decimal? RequiredOnNext, bool IsAchievable) Solve(
        SubjectGradeHistory history,
        GradeScale scale,
        decimal earned,
        decimal pendingWeight,
        decimal totalWeight,
        decimal predictedFinal,
        decimal target)
    {
        var span = scale.Max - scale.Min;

        if (span <= 0m || pendingWeight <= 0m || totalWeight <= 0m)
        {
            // Сдавать больше нечего (или вырожденная шкала) — итог уже зафиксирован прогнозом.
            return (null, predictedFinal + Epsilon >= target);
        }

        var targetFraction = (target - scale.Min) / span;
        var requiredFractionPoints = (targetFraction * totalWeight) - earned;
        var requiredAvgOnRemaining = requiredFractionPoints / pendingWeight;

        var nextMax = Math.Max(0m, EarliestPending(history).MaxScore);

        if (requiredAvgOnRemaining <= Epsilon)
        {
            // Цель обеспечена даже при нуле на всём оставшемся.
            return (0m, true);
        }

        if (requiredAvgOnRemaining > 1m + Epsilon)
        {
            // Даже максимум на всех оставшихся аттестациях не вытягивает на цель.
            return (nextMax, false);
        }

        return (Round(Math.Clamp(requiredAvgOnRemaining * nextMax, 0m, nextMax)), true);
    }

    private static PendingAssessment EarliestPending(SubjectGradeHistory history)
    {
        PendingAssessment? earliest = null;
        foreach (var pending in history.Pending)
        {
            if (earliest is null || pending.Date < earliest.Date)
            {
                earliest = pending;
            }
        }

        return earliest!;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
