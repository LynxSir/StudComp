using StudComp.Core.Domain;

namespace StudComp.Services;

/// <summary>
/// Предмет, который «идёт сейчас по времени» (new_addons.md §1.8) — либо закреплён вручную. Готовые
/// подписи собирает вызывающий.
/// </summary>
public sealed record ActiveSubjectInfo(
    Guid SubjectId,
    string SubjectName,
    string SubjectColorHex,
    bool IsOnNow,
    TimeSpan? StartsIn,
    TimeSpan? EndsIn,
    ScheduleEntryType? ClassType,
    bool IsPinned);

/// <summary>
/// Отслеживает активный предмет по расписанию и текущему времени, обновляется по минутному таймеру и
/// смене расписания (new_addons.md §1.8). Потребители — чип в титул-баре и зона Дашборда.
/// </summary>
public interface IActiveSubjectProvider
{
    /// <summary>Текущий активный предмет либо <see langword="null"/>, если пар нет и ничего не закреплено.</summary>
    ActiveSubjectInfo? Current { get; }

    /// <summary>Срабатывает при смене <see cref="Current"/>.</summary>
    event EventHandler? Changed;

    /// <summary>Закрепить предмет вручную (семинар не по расписанию). <see langword="null"/> — снять закрепление.</summary>
    void PinSubject(Guid? subjectId);

    /// <summary>Немедленно пересчитать активный предмет (напр. после правки расписания).</summary>
    Task RefreshAsync(CancellationToken ct = default);
}
