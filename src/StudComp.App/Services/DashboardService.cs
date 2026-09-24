using System.IO;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;

namespace StudComp.Services;

/// <inheritdoc cref="IDashboardService"/>
internal sealed class DashboardService(
    IScheduleService scheduleService,
    IDeadlineService deadlineService,
    INoteService noteService,
    ISubjectService subjectService,
    IUnsortedFileService unsortedFileService,
    IActivityRepository activityRepository,
    IActiveSubjectProvider activeSubjectProvider,
    IStudyWorkspace studyWorkspace,
    ISemesterService semesterService,
    ICardService cardService,
    ICramPlanService cramPlanService) : IDashboardService
{
    private static readonly TimeSpan DeadlineWindow = TimeSpan.FromDays(14);
    private static readonly TimeSpan UrgentWithin = TimeSpan.FromHours(48);

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var subjects = (await subjectService.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(s => s.Id);

        // Девять независимых запросов (ни один не читает результат другого — все берут уже готовый
        // subjects-словарь) идут параллельно: каждый репозиторий сам открывает свой DbContext через
        // IDbContextFactory, поэтому параллельные обращения безопасны (Phase 13.5, «быстро»).
        var todayClassesTask = BuildTodayAsync(subjects, ct);
        var deadlinesTask = BuildDeadlinesAsync(subjects, ct);
        var recentFilesTask = BuildRecentFilesAsync(ct);
        var unsortedTask = unsortedFileService.GetUnsortedAsync(ct);
        var sortedTask = BuildSortedAsync(ct);
        var recentNotesTask = BuildNotesAsync(subjects, ct);
        var recentReportsTask = BuildRecentReportsAsync(ct);
        var cardsDueTask = cardService.CountDueAsync(ct);
        var cardOfTheDayTask = BuildCardOfTheDayAsync(subjects, ct);
        var countdownsTask = cramPlanService.GetAllAsync(2, ct);

        await Task.WhenAll(
            todayClassesTask, deadlinesTask, recentFilesTask, unsortedTask, sortedTask,
            recentNotesTask, recentReportsTask, cardsDueTask, cardOfTheDayTask, countdownsTask)
            .ConfigureAwait(false);

        var (sortedToday, recentSorted) = await sortedTask.ConfigureAwait(false);

        return new DashboardSnapshot(
            studyWorkspace.HasStudyRoot,
            studyWorkspace.StudyRootPath,
            activeSubjectProvider.Current,
            (await unsortedTask.ConfigureAwait(false)).Count,
            await todayClassesTask.ConfigureAwait(false),
            await deadlinesTask.ConfigureAwait(false),
            await recentFilesTask.ConfigureAwait(false),
            sortedToday,
            recentSorted,
            await recentNotesTask.ConfigureAwait(false),
            await recentReportsTask.ConfigureAwait(false),
            await cardsDueTask.ConfigureAwait(false),
            await cardOfTheDayTask.ConfigureAwait(false),
            await countdownsTask.ConfigureAwait(false));
    }

    /// <summary>
    /// «Карточка дня» (new_addons.md §2.3): берётся из тех, которые уже забывали, и не меняется в
    /// течение суток — выбор делает сам модуль, здесь только перевод в модель Дашборда.
    /// </summary>
    private async Task<DashboardCard?> BuildCardOfTheDayAsync(
        IReadOnlyDictionary<Guid, Subject> subjects,
        CancellationToken ct)
    {
        var card = await cardService
            .GetCardOfTheDayAsync(DateOnly.FromDateTime(DateTime.Today), ct)
            .ConfigureAwait(false);

        if (card is null)
        {
            return null;
        }

        var subject = card.SubjectId is { } id && subjects.TryGetValue(id, out var found) ? found : null;

        return new DashboardCard(
            card.Id,
            card.Front,
            card.Back,
            subject?.Name ?? string.Empty,
            subject?.ColorHex ?? string.Empty,
            card.Lapses > 0);
    }

    private async Task<IReadOnlyList<DashboardClass>> BuildTodayAsync(
        IReadOnlyDictionary<Guid, Subject> subjects, CancellationToken ct)
    {
        var entries = await scheduleService.GetAllAsync(ct).ConfigureAwait(false);
        var semester = await semesterService.GetActiveAsync(ct).ConfigureAwait(false);
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        var nowTime = TimeOnly.FromDateTime(now.DateTime);

        return entries
            .Where(e => e.DayOfWeek == today.DayOfWeek && AppliesToday(e, today, semester))
            .OrderBy(e => e.StartTime)
            .Select(e => new DashboardClass(
                e.Id,
                e.SubjectId,
                subjects.TryGetValue(e.SubjectId, out var s) ? s.Name : "—",
                subjects.TryGetValue(e.SubjectId, out var sc) ? sc.ColorHex : string.Empty,
                e.StartTime,
                e.EndTime,
                e.Room,
                e.Type,
                e.StartTime <= nowTime && nowTime < e.EndTime))
            .ToArray();
    }

    private static bool AppliesToday(ScheduleEntry entry, DateOnly today, Semester? semester)
    {
        // Семестр не настроен — чётность считать не от чего, показываем пару в любой день недели.
        if (entry.WeekParity == WeekParity.Any || semester is null)
        {
            return true;
        }

        return WeekParityCalculator.GetParity(semester.StartDate, today, semester.FirstWeekIsOdd)
            == entry.WeekParity;
    }

    private async Task<IReadOnlyList<DashboardNote>> BuildNotesAsync(
        IReadOnlyDictionary<Guid, Subject> subjects, CancellationToken ct)
    {
        var notes = await noteService.GetRecentAsync(5, ct).ConfigureAwait(false);

        return [.. notes.Select(note => new DashboardNote(
            note.Id,
            note.SubjectId,
            note.SubjectId is { } id && subjects.TryGetValue(id, out var subject) ? subject.Name : "Без предмета",
            note.Title,
            Excerpt(note.ContentMarkdown),
            note.UpdatedAt))];
    }

    /// <summary>Первая содержательная строка заметки — превью в карточке Дашборда.</summary>
    private static string Excerpt(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "пусто";
        }

        var line = markdown
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('#', '>', '-', '*', ' '))
            .FirstOrDefault(x => x.Length > 0) ?? "пусто";

        return line.Length > 90 ? line[..90] + "…" : line;
    }

    private async Task<IReadOnlyList<DashboardDeadline>> BuildDeadlinesAsync(
        IReadOnlyDictionary<Guid, Subject> subjects, CancellationToken ct)
    {
        var upcoming = await deadlineService.GetUpcomingAsync(DeadlineWindow, ct).ConfigureAwait(false);
        var now = DateTimeOffset.Now;

        return upcoming
            .OrderBy(d => d.DueDate)
            .Take(6)
            .Select(d => new DashboardDeadline(
                d.Id,
                d.SubjectId,
                subjects.TryGetValue(d.SubjectId, out var s) ? s.Name : "—",
                d.Title,
                d.DueDate,
                d.DueDate < now,
                d.DueDate >= now && d.DueDate - now <= UrgentWithin))
            .ToArray();
    }

    /// <summary>
    /// Виджет «Архивариус сегодня» (Phase 12.2, new_addons.md §4): сколько файлов разложено сегодня и
    /// топ-5 последних — тем же <c>ActivityKind.FileSorted</c>-срезом, что и «Последние файлы», без
    /// похода в <c>Data</c> (ретеншн ленты ≤500 записей делает клиентский подсчёт корректным).
    /// </summary>
    private async Task<(int Today, IReadOnlyList<DashboardFile> Recent)> BuildSortedAsync(CancellationToken ct)
    {
        var entries = await activityRepository
            .GetRecentByKindsAsync(500, [ActivityKind.FileSorted], ct)
            .ConfigureAwait(false);

        var today = DateTimeOffset.Now.Date;
        var todayCount = entries.Count(e => e.Timestamp.LocalDateTime.Date == today);
        var recent = entries
            .Take(5)
            .Select(e => new DashboardFile(e.Path ?? string.Empty, e.Title ?? Path.GetFileName(e.Path ?? string.Empty), e.Timestamp, e.Kind))
            .ToArray();

        return (todayCount, recent);
    }

    /// <summary>
    /// Виджет «Последние отчёты» (new_addons.md §6): последние 5 генераций отчётов тем же срезом
    /// ленты активности, что и остальные зоны Дашборда — <c>NewReportViewModel</c> пишет
    /// <c>ActivityKind.ReportGenerated</c> при каждом успешном запуске.
    /// </summary>
    private async Task<IReadOnlyList<DashboardFile>> BuildRecentReportsAsync(CancellationToken ct)
    {
        var entries = await activityRepository
            .GetRecentByKindsAsync(5, [ActivityKind.ReportGenerated], ct)
            .ConfigureAwait(false);

        return [.. entries.Select(e =>
            new DashboardFile(e.Path ?? string.Empty, e.Title ?? Path.GetFileName(e.Path ?? string.Empty), e.Timestamp, e.Kind))];
    }

    private async Task<IReadOnlyList<DashboardFile>> BuildRecentFilesAsync(CancellationToken ct)
    {
        var entries = await activityRepository
            .GetRecentByKindsAsync(30, [ActivityKind.FileOpened, ActivityKind.FileSorted], ct)
            .ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DashboardFile>();
        foreach (var e in entries)
        {
            var path = e.Path ?? string.Empty;
            if (path.Length == 0 || !seen.Add(path))
            {
                continue;
            }

            result.Add(new DashboardFile(path, e.Title ?? Path.GetFileName(path), e.Timestamp, e.Kind));
            if (result.Count == 8)
            {
                break;
            }
        }

        return result;
    }
}
