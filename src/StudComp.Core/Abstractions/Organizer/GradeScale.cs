namespace StudComp.Core.Abstractions.Organizer;

/// <summary>
/// Используемая система оценивания. Пятибалльная не зашита намеренно: факультеты различаются,
/// стобалльная (в духе ECTS) встречается не реже (ARCHITECTURE §9.4).
/// </summary>
public enum GradeScaleKind
{
    FivePoint = 0,
    HundredPoint = 1,
    Custom = 2,
}

/// <summary>
/// Границы шкалы и порог сдачи (ARCHITECTURE §9.4).
/// </summary>
/// <param name="Min">Минимально возможная оценка.</param>
/// <param name="Max">Максимально возможная оценка.</param>
/// <param name="PassThreshold">Наименьшая оценка, которая ещё считается сдачей.</param>
/// <param name="Kind">Какая это система — для отображения и значений по умолчанию.</param>
public record GradeScale(decimal Min, decimal Max, decimal PassThreshold, GradeScaleKind Kind)
{
    /// <summary>
    /// Стандартные границы для известных систем: 5-балльная — <c>(2, 5, 3)</c>, 100-балльная — <c>(0, 100, 60)</c>.
    /// Для <see cref="GradeScaleKind.Custom"/> возвращает тот же ориентир, что и 100-балльная — вызывающий
    /// (обычно <c>GradeBookService</c>) подменяет границы значениями предмета (ARCHITECTURE §9.4).
    /// </summary>
    public static GradeScale For(GradeScaleKind kind) => kind switch
    {
        GradeScaleKind.FivePoint => new GradeScale(2m, 5m, 3m, GradeScaleKind.FivePoint),
        GradeScaleKind.HundredPoint => new GradeScale(0m, 100m, 60m, GradeScaleKind.HundredPoint),
        GradeScaleKind.Custom => new GradeScale(0m, 100m, 60m, GradeScaleKind.Custom),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестная система оценивания."),
    };
}
