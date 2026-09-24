using StudComp.Core.Domain;

namespace StudComp.Core.Abstractions.Organizer;

/// <summary>
/// Аттестация, которая запланирована, но ещё не оценена — то, на что экстраполируется прогноз
/// (ARCHITECTURE §9.4).
/// </summary>
/// <param name="Name">Отображаемое название, например «Лабораторная 4».</param>
/// <param name="MaxScore">Максимально возможный балл.</param>
/// <param name="Weight">Вес в итоговой оценке, в тех же единицах, что и <see cref="GradeEntry.Weight"/>.</param>
/// <param name="Date">Дата сдачи.</param>
public record PendingAssessment(string Name, decimal MaxScore, decimal Weight, DateTime Date);

/// <summary>
/// Всё, что нужно <see cref="IGradeForecastStrategy"/> по одному предмету: что уже оценено
/// и что ещё предстоит (ARCHITECTURE §9.4).
/// </summary>
public record SubjectGradeHistory(
    Guid SubjectId,
    IReadOnlyList<GradeEntry> Entries,
    IReadOnlyList<PendingAssessment> Pending);
