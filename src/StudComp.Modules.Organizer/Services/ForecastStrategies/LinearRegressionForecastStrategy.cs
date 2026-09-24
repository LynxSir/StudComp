using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Domain;

namespace StudComp.Modules.Organizer.Services.ForecastStrategies;

/// <summary>
/// Стратегия прогноза с учётом тренда (ARCHITECTURE §9.4, §15 «Этап 3»): по оценённым строкам строит
/// взвешенную линейную регрессию доли <c>RawScore / MaxScore</c> от времени и продлевает прямую на даты
/// ещё не сданных аттестаций. Для предмета, где студент «стал сдавать лучше/хуже», даёт прогноз, отличный
/// от средневзвешенной. Обратная задача — общий <see cref="ReverseTaskSolver"/> (ответ «что сдать» от
/// стратегии не зависит). Чистая арифметика без I/O — синхронная и без побочных эффектов.
/// </summary>
internal sealed class LinearRegressionForecastStrategy : IGradeForecastStrategy
{
    /// <summary>Ключ, под которым стратегия регистрируется как keyed-сервис и хранится в настройках предмета.</summary>
    public const string Key = "linear-regression";

    /// <summary>Доля веса уже оценённого, начиная с которой прогнозу доверяем «высоко».</summary>
    private const decimal HighHistoryShare = 0.60m;

    /// <summary>Доля веса уже оценённого, начиная с которой прогнозу доверяем «средне».</summary>
    private const decimal MediumHistoryShare = 0.25m;

    /// <summary>Ниже этого R² прямая плохо описывает разброс — уверенность снижаем на уровень.</summary>
    private const double WeakFitRSquared = 0.30d;

    public string Name => Key;

    public ForecastResult Forecast(SubjectGradeHistory history, GradeScale scale, decimal? targetFinalGrade = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(scale);

        var span = scale.Max - scale.Min;

        // Оценённые строки по возрастанию даты → точки регрессии: x = день от первой оценки, y = доля, вес.
        var graded = history.Entries.Where(e => !e.IsPlanned).OrderBy(e => e.Date).ToList();
        DateTime? firstDate = graded.Count > 0 ? graded[0].Date : null;

        var xs = new List<double>(graded.Count);
        var ys = new List<double>(graded.Count);
        var ws = new List<double>(graded.Count);

        decimal gradedWeight = 0m;
        decimal earned = 0m; // Σ (доля_i · вес_i)
        foreach (var entry in graded)
        {
            var weight = Math.Max(0m, entry.Weight);
            var fraction = entry.MaxScore > 0m
                ? Math.Clamp(entry.RawScore / entry.MaxScore, 0m, 1m)
                : 0m;

            xs.Add((entry.Date - firstDate!.Value).TotalDays);
            ys.Add((double)fraction);
            ws.Add((double)weight);

            gradedWeight += weight;
            earned += fraction * weight;
        }

        var pendingWeight = history.Pending.Sum(p => Math.Max(0m, p.Weight));
        var totalWeight = gradedWeight + pendingWeight;

        var fit = LinearRegression.Fit(xs, ys, ws);

        // --- Прямой прогноз ---------------------------------------------------------------------
        decimal predictedFinal;
        if (totalWeight <= 0m || span <= 0m || graded.Count == 0)
        {
            predictedFinal = scale.Min;
        }
        else if (pendingWeight <= 0m)
        {
            // Сдавать нечего — итог = уже набранная средняя доля.
            predictedFinal = scale.Min + (Math.Clamp(earned / gradedWeight, 0m, 1m) * span);
        }
        else
        {
            // Несданное оцениваем по тренду: продлеваем прямую на дату каждой аттестации.
            decimal projectedPending = 0m;
            foreach (var pending in history.Pending)
            {
                var days = (pending.Date - firstDate!.Value).TotalDays;
                var projectedFraction = (decimal)Math.Clamp(fit.PredictAt(days), 0d, 1d);
                projectedPending += projectedFraction * Math.Max(0m, pending.Weight);
            }

            var predictedFraction = (earned + projectedPending) / totalWeight;
            predictedFinal = scale.Min + (Math.Clamp(predictedFraction, 0m, 1m) * span);
        }

        predictedFinal = Round(predictedFinal);

        // --- Уверенность: доля истории, но с капом по качеству модели -------------------------
        var confidence = ConfidenceFor(gradedWeight, totalWeight, graded.Count, fit.RSquared);

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

    /// <summary>
    /// Уверенность = бакеты по доле оценённого веса (как у средневзвешенной), но регрессия по 2–3 точкам
    /// не бывает «высокой», а слабая подгонка (низкий R²) снижает уровень ещё на ступень.
    /// </summary>
    private static ConfidenceLevel ConfidenceFor(
        decimal gradedWeight, decimal totalWeight, int gradedCount, double rSquared)
    {
        if (gradedWeight <= 0m || gradedCount < 2)
        {
            return ConfidenceLevel.Low;
        }

        var historyShare = totalWeight > 0m ? gradedWeight / totalWeight : 0m;
        var level = historyShare >= HighHistoryShare
            ? ConfidenceLevel.High
            : historyShare >= MediumHistoryShare
                ? ConfidenceLevel.Medium
                : ConfidenceLevel.Low;

        if (gradedCount < 4 && level == ConfidenceLevel.High)
        {
            level = ConfidenceLevel.Medium;
        }

        if (rSquared < WeakFitRSquared && level > ConfidenceLevel.Low)
        {
            level--;
        }

        return level;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
