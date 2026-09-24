using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Services;

/// <summary>Пара в сегодняшнем таймлайне Дашборда.</summary>
public sealed record DashboardClass(
    Guid ScheduleEntryId,
    Guid SubjectId,
    string SubjectName,
    string ColorHex,
    TimeOnly Start,
    TimeOnly End,
    string Room,
    ScheduleEntryType Type,
    bool IsOnNow);

/// <summary>Горящий дедлайн для Дашборда.</summary>
public sealed record DashboardDeadline(
    Guid Id,
    Guid SubjectId,
    string SubjectName,
    string Title,
    DateTimeOffset DueDate,
    bool IsOverdue,
    bool IsUrgent);

/// <summary>Недавно открытый или отсортированный файл.</summary>
public sealed record DashboardFile(string Path, string Title, DateTimeOffset When, ActivityKind Kind);

/// <summary>Недавно изменённая заметка для Дашборда.</summary>
public sealed record DashboardNote(
    Guid Id, Guid? SubjectId, string SubjectName, string Title, string Excerpt, DateTimeOffset UpdatedAt);

/// <summary>Карточка знания для Дашборда — «карточка дня» (new_addons.md §2.3).</summary>
/// <param name="Id">Сама карточка: по ней делается переход в раздел.</param>
/// <param name="Front">Лицевая сторона.</param>
/// <param name="Back">Оборот — показывается только по нажатию.</param>
/// <param name="SubjectName">Предмет или пусто.</param>
/// <param name="ColorHex">Цвет предмета для полосы.</param>
/// <param name="IsWeak">Взята ли она из тех, которые уже забывали.</param>
public sealed record DashboardCard(
    Guid Id, string Front, string Back, string SubjectName, string ColorHex, bool IsWeak);

/// <summary>Сводка для Дашборда — агрегируется из модулей, своих данных не хранит (new_addons.md §1.5).</summary>
public sealed record DashboardSnapshot(
    bool HasStudyRoot,
    string StudyRootPath,
    ActiveSubjectInfo? ActiveSubject,
    int UnsortedCount,
    IReadOnlyList<DashboardClass> TodayClasses,
    IReadOnlyList<DashboardDeadline> HotDeadlines,
    IReadOnlyList<DashboardFile> RecentFiles,
    int SortedTodayCount,
    IReadOnlyList<DashboardFile> RecentSortedFiles,
    IReadOnlyList<DashboardNote> RecentNotes,
    IReadOnlyList<DashboardFile> RecentReports,
    int CardsDueCount,
    DashboardCard? CardOfTheDay,
    IReadOnlyList<CramStatus> ExamCountdowns);

/// <summary>Собирает <see cref="DashboardSnapshot"/> из сервисов модулей и репозиториев (App-уровень).</summary>
public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
