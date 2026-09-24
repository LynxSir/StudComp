namespace StudComp.Core.Abstractions.Organizer;

/// <summary>
/// Насколько прогнозу можно доверять — определяется тем, сколько истории под ним было
/// (ARCHITECTURE §9.4).
/// </summary>
public enum ConfidenceLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// Прогноз итоговой оценки по предмету (ARCHITECTURE §9.4).
/// </summary>
/// <param name="PredictedFinalScore">Ожидаемый итог, если темп сохранится.</param>
/// <param name="RequiredScoreOnNextAssessment">
/// Сколько нужно набрать на ближайшей аттестации, чтобы выйти на цель; кламп к <c>[0, MaxScore]</c>.
/// <see langword="null"/>, если цель не задана или сдавать больше нечего.
/// </param>
/// <param name="Confidence">Уровень доверия к прогнозу.</param>
/// <param name="StrategyUsed">Имя стратегии, выдавшей результат.</param>
/// <param name="IsAchievable">
/// <see langword="false"/>, если цель недостижима даже при максимуме на всём оставшемся — UI честно
/// говорит «уже не дотянуть» вместо невыполнимого требуемого балла (ARCHITECTURE §9.4).
/// </param>
public record ForecastResult(
    decimal PredictedFinalScore,
    decimal? RequiredScoreOnNextAssessment,
    ConfidenceLevel Confidence,
    string StrategyUsed,
    bool IsAchievable = true);
