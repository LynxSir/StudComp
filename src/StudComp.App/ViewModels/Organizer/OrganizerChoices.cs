using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Domain;
using StudComp.ViewModels;

namespace StudComp.ViewModels.Organizer;

/// <summary>Готовые списки вариантов для выпадающих списков форм расписания и дедлайнов.</summary>
public static class OrganizerChoices
{
    public static IReadOnlyList<NamedChoice<DayOfWeek>> Days { get; } =
    [
        new(DayOfWeek.Monday, "Понедельник"),
        new(DayOfWeek.Tuesday, "Вторник"),
        new(DayOfWeek.Wednesday, "Среда"),
        new(DayOfWeek.Thursday, "Четверг"),
        new(DayOfWeek.Friday, "Пятница"),
        new(DayOfWeek.Saturday, "Суббота"),
        new(DayOfWeek.Sunday, "Воскресенье"),
    ];

    public static IReadOnlyList<NamedChoice<ScheduleEntryType>> ScheduleTypes { get; } =
    [
        new(ScheduleEntryType.Lecture, "Лекция"),
        new(ScheduleEntryType.Seminar, "Семинар"),
        new(ScheduleEntryType.Lab, "Лабораторная"),
        new(ScheduleEntryType.Practice, "Практика"),
        new(ScheduleEntryType.Consultation, "Консультация"),
        new(ScheduleEntryType.Exam, "Экзамен"),
    ];

    /// <summary>
    /// Подпись типа занятия. Отдельный метод с запасным вариантом — чтобы новое значение перечисления
    /// не роняло плитку расписания, если его забыли добавить в список выше.
    /// </summary>
    public static string ScheduleTypeName(ScheduleEntryType type) =>
        ScheduleTypes.FirstOrDefault(x => x.Value == type)?.Display ?? "Занятие";

    public static IReadOnlyList<NamedChoice<WeekParity>> Parities { get; } =
    [
        new(WeekParity.Any, "Каждую неделю"),
        new(WeekParity.Odd, "Числитель"),
        new(WeekParity.Even, "Знаменатель"),
    ];

    public static IReadOnlyList<NamedChoice<DeadlineType>> DeadlineTypes { get; } =
    [
        new(DeadlineType.Homework, "Домашняя работа"),
        new(DeadlineType.Test, "Контрольная"),
        new(DeadlineType.Exam, "Экзамен"),
        new(DeadlineType.CourseWork, "Курсовая"),
        new(DeadlineType.Other, "Другое"),
    ];

    public static IReadOnlyList<NamedChoice<DeadlinePriority>> Priorities { get; } =
    [
        new(DeadlinePriority.Low, "Низкий"),
        new(DeadlinePriority.Normal, "Обычный"),
        new(DeadlinePriority.High, "Высокий"),
    ];

    public static IReadOnlyList<NamedChoice<GradeEntryType>> GradeTypes { get; } =
    [
        new(GradeEntryType.Exam, "Экзамен"),
        new(GradeEntryType.Test, "Зачёт / контрольная"),
        new(GradeEntryType.CourseWork, "Курсовая"),
        new(GradeEntryType.Attestation, "Аттестация"),
    ];

    public static IReadOnlyList<NamedChoice<GradeScaleKind>> GradeScaleKinds { get; } =
    [
        new(GradeScaleKind.FivePoint, "5-балльная"),
        new(GradeScaleKind.HundredPoint, "100-балльная"),
        new(GradeScaleKind.Custom, "Произвольная"),
    ];

    /// <summary>
    /// Стратегии прогноза итоговой оценки (ARCHITECTURE §9.4). Значение — ключ keyed-сервиса
    /// (<c>IGradeForecastStrategy.Name</c>); пустая строка = стратегия по умолчанию. Ключи заданы
    /// литералами: классы стратегий <c>internal</c> в модуле Органайзера и App их констант не видит.
    /// </summary>
    public static IReadOnlyList<NamedChoice<string>> ForecastStrategies { get; } =
    [
        new(string.Empty, "Средневзвешенная — по умолчанию"),
        new("linear-regression", "Линейная регрессия — учёт тренда"),
    ];

    /// <summary>Русское название дня недели для подписей в UI.</summary>
    public static string DayName(DayOfWeek day) => Days.First(d => d.Value == day).Display;
}
