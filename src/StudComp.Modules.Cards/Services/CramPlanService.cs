using Microsoft.Extensions.Options;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Обратный отсчёт к экзамену по одному предмету (new_addons.md §6.4).
/// </summary>
/// <param name="AchievableShows">
/// Сколько раз каждую карточку успеть показать на самом деле. Меньше <see cref="RequestedShows"/> —
/// значит план не сходится, и говорить об этом надо прямо.
/// </param>
public sealed record CramStatus(
    Guid SubjectId,
    string SubjectName,
    Guid DeadlineId,
    string DeadlineTitle,
    DateTimeOffset ExamAt,
    int DaysLeft,
    int CardCount,
    int RequestedShows,
    int AchievableShows,
    bool IsAchievable,
    bool IsExpired,
    int CardsToday,
    int ProgressPercent);

/// <summary>
/// «Аврал»: раскладка материала предмета по дням, оставшимся до экзамена (new_addons.md §6.4).
/// </summary>
/// <remarks>
/// Единственное место, где Картотека узнаёт о данных Органайзера. Прямой ссылки между модулями это
/// не создаёт: дедлайны читаются из <c>StudComp.Data</c>, на который модуль ссылается и так по графу
/// §5.1 — тот же приём, каким <c>CardService</c> уже читает предметы.
/// </remarks>
public interface ICramPlanService
{
    /// <summary>Состояние аврала по предмету; <see langword="null"/> — ближайшего экзамена нет.</summary>
    Task<CramStatus?> GetStatusAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>Ближайшие экзамены со своими планами — зона Дашборда и вкладка «Повторение».</summary>
    Task<IReadOnlyList<CramStatus>> GetAllAsync(int take = 3, CancellationToken ct = default);

    /// <summary>Сегодняшняя порция материала как готовый список карточек.</summary>
    Task<Result<IReadOnlyList<Guid>>> GetTodayAsync(Guid subjectId, CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class CramPlanService(
    ICardRepository cards,
    ICardReviewLogRepository logs,
    IDeadlineRepository deadlines,
    ISubjectRepository subjects,
    IOptionsMonitor<CardsOptions> options) : ICramPlanService
{
    /// <summary>За сколько дней до экзамена аврал вообще имеет смысл показывать.</summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(60);

    /// <summary>Окно, в котором засчитывается прогресс подготовки к этому экзамену.</summary>
    private static readonly TimeSpan ProgressWindow = TimeSpan.FromDays(90);

    public async Task<CramStatus?> GetStatusAsync(Guid subjectId, CancellationToken ct = default)
    {
        var exam = await FindNearestExamAsync(subjectId, ct).ConfigureAwait(false);

        return exam is null ? null : await BuildAsync(exam, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CramStatus>> GetAllAsync(int take = 3, CancellationToken ct = default)
    {
        var upcoming = await deadlines.GetUpcomingAsync(Horizon, ct).ConfigureAwait(false);

        var exams = upcoming
            .Where(d => d.Type == DeadlineType.Exam && d.Status == DeadlineStatus.Pending)
            .GroupBy(d => d.SubjectId)
            .Select(g => g.OrderBy(d => d.DueDate).First())
            .OrderBy(d => d.DueDate)
            .Take(Math.Max(1, take))
            .ToList();

        var result = new List<CramStatus>(exams.Count);
        foreach (var exam in exams)
        {
            var status = await BuildAsync(exam, ct).ConfigureAwait(false);
            if (status is not null)
            {
                result.Add(status);
            }
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<Guid>>> GetTodayAsync(
        Guid subjectId,
        CancellationToken ct = default)
    {
        var exam = await FindNearestExamAsync(subjectId, ct).ConfigureAwait(false);
        if (exam is null)
        {
            return Result<IReadOnlyList<Guid>>.Failure(
                "cards.cram_no_exam",
                "У предмета нет ближайшего экзамена — готовиться не к чему.");
        }

        var status = await BuildAsync(exam, ct).ConfigureAwait(false);
        if (status is null || status.CardCount == 0)
        {
            return Result<IReadOnlyList<Guid>>.Failure(
                "cards.session_empty",
                "По предмету нет ни одной карточки.");
        }

        var pool = await cards.GetStudyCandidatesAsync(new StudyPoolFilter(SubjectIds: [subjectId]), ct)
            .ConfigureAwait(false);

        var cycle = ExamCountdownPlanner.BuildCycle(pool, status.RequestedShows, exam.Id.GetHashCode());
        var progress = await logs.GetCramProgressAsync(subjectId, DateTimeOffset.Now - ProgressWindow, ct)
            .ConfigureAwait(false);

        var slice = ExamCountdownPlanner.Slice(cycle, progress.Shows, status.CardsToday);

        return slice.Count == 0
            ? Result<IReadOnlyList<Guid>>.Failure("cards.session_empty", "На сегодня материал уже пройден.")
            : Result<IReadOnlyList<Guid>>.Success(slice);
    }

    private async Task<Deadline?> FindNearestExamAsync(Guid subjectId, CancellationToken ct)
    {
        var all = await deadlines.GetBySubjectAsync(subjectId, ct).ConfigureAwait(false);

        return all
            .Where(d => d.Type == DeadlineType.Exam && d.Status == DeadlineStatus.Pending)
            .OrderBy(d => d.DueDate)
            .FirstOrDefault(d => d.DueDate >= DateTimeOffset.Now - TimeSpan.FromDays(1));
    }

    private async Task<CramStatus?> BuildAsync(Deadline exam, CancellationToken ct)
    {
        var subject = await subjects.GetByIdAsync(exam.SubjectId, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return null;
        }

        var settings = options.CurrentValue;
        var now = DateTimeOffset.Now;

        var cardCount = await cards.CountBySubjectAsync(exam.SubjectId, ct).ConfigureAwait(false);
        var progress = await logs.GetCramProgressAsync(exam.SubjectId, now - ProgressWindow, ct)
            .ConfigureAwait(false);

        var daysLeft = StudyDay.DayOf(exam.DueDate.ToLocalTime(), settings.DayRolloverHour).DayNumber
            - StudyDay.DayOf(now, settings.DayRolloverHour).DayNumber;

        // Потолок дня — тот же дневной лимит повторений, что и в обычной очереди: за сутки человек
        // физически не прогонит больше, и делать вид, что прогонит, планировщик не имеет права.
        var plan = ExamCountdownPlanner.Plan(new CramPlanRequest(
            CardCount: cardCount,
            DaysLeft: daysLeft,
            MinShowsPerCard: settings.CramMinShows,
            ShowsDone: progress.Shows,
            MaxCardsPerDay: settings.ReviewsPerDay));

        return new CramStatus(
            subject.Id,
            subject.Name,
            exam.Id,
            exam.Title,
            exam.DueDate,
            daysLeft,
            cardCount,
            plan.RequestedShowsPerCard,
            plan.AchievableShowsPerCard,
            plan.IsAchievable,
            plan.IsExpired,
            plan.CardsToday,
            plan.ProgressPercent);
    }
}
