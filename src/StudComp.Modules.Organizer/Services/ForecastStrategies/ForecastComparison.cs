using System.Globalization;
using StudComp.Core.Abstractions.Organizer;

namespace StudComp.Modules.Organizer.Services.ForecastStrategies;

/// <summary>
/// Однострочное объяснение разницы между стратегиями прогноза для карточки зачётки (DoD Phase 11:
/// «разница в прогнозе объяснима пользователю простыми словами»). Чистая функция форматирования;
/// публичная — её вызывает VM зачётки в App.
/// </summary>
public static class ForecastComparison
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Ниже этого наклона (доля/день) тренд считаем плоским.</summary>
    private const double FlatSlopePerDay = 1e-4d;

    /// <summary>
    /// Строит фразу вида «Средневзвешенная 3,8 · линейная регрессия 4,3. Оценки растут — линейная
    /// регрессия это учитывает.» <paramref name="trendSlopePerDay"/> — наклон линии тренда (доля/день).
    /// </summary>
    public static string Describe(
        ForecastResult weighted,
        ForecastResult linear,
        GradeScale scale,
        double trendSlopePerDay)
    {
        var span = scale.Max - scale.Min;
        var diff = linear.PredictedFinalScore - weighted.PredictedFinalScore;
        var near = Math.Abs(diff) < (span > 0m ? 0.05m * span : 0.05m);

        var w = weighted.PredictedFinalScore.ToString("0.##", Ru);
        var l = linear.PredictedFinalScore.ToString("0.##", Ru);
        var head = $"Средневзвешенная {w} · линейная регрессия {l}.";

        if (near || Math.Abs(trendSlopePerDay) < FlatSlopePerDay)
        {
            return $"{head} Оценки держатся ровно — обе стратегии почти совпадают.";
        }

        var trend = trendSlopePerDay > 0d ? "растут" : "снижаются";
        return $"{head} Оценки {trend} — линейная регрессия это учитывает.";
    }
}
