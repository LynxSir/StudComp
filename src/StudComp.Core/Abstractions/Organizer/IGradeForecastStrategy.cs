namespace StudComp.Core.Abstractions.Organizer;

/// <summary>
/// Прогнозирует итоговую оценку по истории предмета. Паттерн «Стратегия»: реализации регистрируются
/// как keyed-сервисы и выбираются в настройках предмета, по умолчанию — средневзвешенная (ARCHITECTURE §9.4).
/// Синхронный намеренно: чистая арифметика по уже загруженным данным, без I/O.
/// </summary>
public interface IGradeForecastStrategy
{
    /// <summary>Устойчивый ключ, под которым стратегия регистрируется и сохраняется в настройках.</summary>
    string Name { get; }

    /// <summary>
    /// Считает прогноз, а если задан <paramref name="targetFinalGrade"/> — решает и обратную задачу
    /// «сколько нужно набрать на ближайшей аттестации» (ARCHITECTURE §9.4).
    /// </summary>
    ForecastResult Forecast(SubjectGradeHistory history, GradeScale scale, decimal? targetFinalGrade = null);
}
