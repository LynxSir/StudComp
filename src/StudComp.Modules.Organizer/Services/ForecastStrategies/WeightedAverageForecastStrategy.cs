using StudComp.Core.Abstractions.Organizer;

namespace StudComp.Modules.Organizer.Services.ForecastStrategies;

/// <summary>
/// Стратегия прогноза по умолчанию (ARCHITECTURE §9.4): каждую оценку нормализует в долю
/// <c>RawScore / MaxScore</c>, взвешивает по <c>Weight</c> и переносит средний темп на ещё не
/// сданные аттестации. Обратная задача («сколько нужно на ближайшей, чтобы выйти на цель»)
/// решается аналитически и обобщена на случай нескольких оставшихся аттестаций: считается
/// требуемая средняя доля по всем оставшимся, выраженная баллом на ближайшей по дате. При
/// единственной планируемой аттестации сводится к формуле §9.4.
/// Чистая арифметика без I/O — синхронная и без побочных эффектов.
/// </summary>
internal sealed class WeightedAverageForecastStrategy : IGradeForecastStrategy
{
    /// <summary>Ключ, под которым стратегия регистрируется как keyed-сервис и хранится в настройках предмета.</summary>
    public const string Key = "weighted-average";

    /// <summary>Доля веса уже оценённого, начиная с которой прогнозу доверяем «высоко».</summary>
    private const decimal HighHistoryShare = 0.60m;

    /// <summary>Доля веса уже оценённого, начиная с которой прогнозу доверяем «средне».</summary>
    private const decimal MediumHistoryShare = 0.25m;

    public string Name => Key;

    public ForecastResult Forecast(SubjectGradeHistory history, GradeScale scale, decimal? targetFinalGrade = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(scale);

        var span = scale.Max - scale.Min;

        // Оценённое: доля от максимума, взвешенная. IsPlanned-строки сюда не считаем, даже если попали.
        decimal gradedWeight = 0m;
        decimal earned = 0m; // Σ (доля_i · вес_i)
        foreach (var entry in history.Entries)
        {
            if (entry.IsPlanned)
            {
                continue;
            }

            var weight = Math.Max(0m, entry.Weight);
            var fraction = entry.MaxScore > 0m
                ? Math.Clamp(entry.RawScore / entry.MaxScore, 0m, 1m)
                : 0m;

            gradedWeight += weight;
            earned += fraction * weight;
        }

        var pendingWeight = history.Pending.Sum(p => Math.Max(0m, p.Weight));
        var totalWeight = gradedWeight + pendingWeight;

        var avgFraction = gradedWeight > 0m ? earned / gradedWeight : (decimal?)null;

        // --- Прямой прогноз ---------------------------------------------------------------------
        decimal predictedFinal;
        if (totalWeight <= 0m || span <= 0m)
        {
            predictedFinal = scale.Min;
        }
        else
        {
            // На несданное переносим средний темп; истории нет — честно считаем от нуля.
            var assumedPendingFraction = avgFraction ?? 0m;
            var predictedFraction = (earned + (assumedPendingFraction * pendingWeight)) / totalWeight;
            predictedFinal = scale.Min + (Math.Clamp(predictedFraction, 0m, 1m) * span);
        }

        predictedFinal = Round(predictedFinal);

        // --- Уверенность ----------------------------------------------------------------------
        var historyShare = totalWeight > 0m ? gradedWeight / totalWeight : 0m;
        var confidence = gradedWeight <= 0m
            ? ConfidenceLevel.Low
            : historyShare >= HighHistoryShare
                ? ConfidenceLevel.High
                : historyShare >= MediumHistoryShare
                    ? ConfidenceLevel.Medium
                    : ConfidenceLevel.Low;

        // --- Обратная задача (общий солвер — ответ не зависит от стратегии) -----------------
        decimal? requiredOnNext = null;
        var isAchievable = true;

        if (targetFinalGrade is { } target)
        {
            (requiredOnNext, isAchievable) = ReverseTaskSolver.Solve(
                history, scale, earned, pendingWeight, totalWeight, predictedFinal, target);
        }

        return new ForecastResult(predictedFinal, requiredOnNext, confidence, Key, isAchievable);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
