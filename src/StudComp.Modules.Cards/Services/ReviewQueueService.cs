using Microsoft.Extensions.Options;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Cards.Services;

/// <summary>Счётчики очереди дня — бейдж сайдбара, зона Дашборда и вкладка «Повторение».</summary>
/// <param name="Due">Сколько карточек созрело.</param>
/// <param name="New">Сколько новых ждёт первого показа.</param>
/// <param name="NewRemainingToday">Сколько новых ещё можно ввести сегодня по лимиту.</param>
/// <param name="ReviewsRemainingToday">Сколько повторений ещё осталось сегодня по лимиту.</param>
public sealed record ReviewQueueCounts(int Due, int New, int NewRemainingToday, int ReviewsRemainingToday);

/// <summary>Готовая очередь дня.</summary>
/// <param name="SkippedByLimit">
/// Сколько созревших карточек не влезло в дневной лимит. Показывается честно: молча спрятать
/// полторы сотни карточек хуже, чем сказать о них.
/// </param>
public sealed record ReviewQueueSnapshot(
    IReadOnlyList<Guid> CardIds,
    int DueCount,
    int NewCount,
    int SkippedByLimit,
    DateTimeOffset? NextDueAt);

/// <summary>Очередь интервального повторения (new_addons.md §6.3).</summary>
public interface IReviewQueueService
{
    /// <summary>Счётчики без построения самой очереди — их спрашивают часто и из разных мест.</summary>
    Task<ReviewQueueCounts> GetCountsAsync(CancellationToken ct = default);

    /// <summary>Собрать очередь на сегодня с учётом дневных лимитов.</summary>
    Task<Result<ReviewQueueSnapshot>> BuildAsync(
        Guid? subjectId = null,
        Guid? deckId = null,
        CancellationToken ct = default);

    /// <summary>Сколько карточек придёт на повторение в ближайшие дни — кривая нагрузки (§6.5).</summary>
    Task<IReadOnlyList<DueForecastBucket>> GetForecastAsync(int days = 30, CancellationToken ct = default);

    /// <summary>Когда ближайшее повторение, если на сегодня всё.</summary>
    Task<DateTimeOffset?> GetNextDueAtAsync(CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class ReviewQueueService(
    ICardRepository cards,
    ICardReviewLogRepository logs,
    IOptionsMonitor<CardsOptions> options) : IReviewQueueService
{
    public async Task<ReviewQueueCounts> GetCountsAsync(CancellationToken ct = default)
    {
        var settings = options.CurrentValue;
        var now = DateTimeOffset.Now;
        var dayStart = StudyDay.StartOf(now, settings.DayRolloverHour);

        var due = await cards.CountDueAsync(now, ct).ConfigureAwait(false);
        var fresh = await cards.CountNewAvailableAsync(ct).ConfigureAwait(false);
        var newToday = await logs.CountNewIntroducedSinceAsync(dayStart, ct).ConfigureAwait(false);
        var reviewsToday = await logs.CountReviewsSinceAsync(dayStart, ct).ConfigureAwait(false);

        return new ReviewQueueCounts(
            due,
            fresh,
            Math.Max(0, settings.NewCardsPerDay - newToday),
            Math.Max(0, settings.ReviewsPerDay - reviewsToday));
    }

    public async Task<Result<ReviewQueueSnapshot>> BuildAsync(
        Guid? subjectId = null,
        Guid? deckId = null,
        CancellationToken ct = default)
    {
        var settings = options.CurrentValue;

        if (!settings.ReviewEnabled)
        {
            return Result<ReviewQueueSnapshot>.Failure(
                "cards.review_disabled",
                "Интервальное повторение выключено в настройках Картотеки.");
        }

        var now = DateTimeOffset.Now;
        var dayStart = StudyDay.StartOf(now, settings.DayRolloverHour);

        var newToday = await logs.CountNewIntroducedSinceAsync(dayStart, ct).ConfigureAwait(false);
        var reviewsToday = await logs.CountReviewsSinceAsync(dayStart, ct).ConfigureAwait(false);

        var reviewBudget = Math.Max(0, settings.ReviewsPerDay - reviewsToday);
        var newBudget = Math.Max(0, settings.NewCardsPerDay - newToday);

        IReadOnlyList<Guid> subjects = subjectId is { } s ? [s] : [];
        IReadOnlyList<Guid> decks = deckId is { } d ? [d] : [];

        var mature = reviewBudget == 0
            ? []
            : await cards.GetStudyCandidatesAsync(
                new StudyPoolFilter(
                    SubjectIds: subjects,
                    DeckIds: decks,
                    DueBefore: now,
                    Sort: StudyPoolSort.DueAsc,
                    Limit: reviewBudget),
                ct).ConfigureAwait(false);

        var fresh = newBudget == 0
            ? []
            : await cards.GetStudyCandidatesAsync(
                new StudyPoolFilter(
                    SubjectIds: subjects,
                    DeckIds: decks,
                    OnlyNew: true,
                    Sort: StudyPoolSort.CreatedAsc,
                    Limit: newBudget),
                ct).ConfigureAwait(false);

        var dueTotal = await cards.CountDueAsync(now, ct).ConfigureAwait(false);
        var pool = mature.Concat(fresh).ToList();

        if (pool.Count == 0)
        {
            return Result<ReviewQueueSnapshot>.Success(new ReviewQueueSnapshot(
                [],
                0,
                0,
                Math.Max(0, dueTotal - mature.Count),
                await GetNextDueAtAsync(ct).ConfigureAwait(false)));
        }

        var order = StudySessionPlanner.Build(
            pool,
            new StudyPlanOptions(
                Order: settings.QueueOrder,
                SpreadDecks: true,
                NewCardShare: fresh.Count == 0 ? 0 : settings.NewCardShare),
            unchecked((int)DateTimeOffset.Now.Ticks));

        return Result<ReviewQueueSnapshot>.Success(new ReviewQueueSnapshot(
            order,
            mature.Count,
            fresh.Count,
            Math.Max(0, dueTotal - mature.Count),
            null));
    }

    public Task<IReadOnlyList<DueForecastBucket>> GetForecastAsync(
        int days = 30,
        CancellationToken ct = default)
    {
        var settings = options.CurrentValue;
        var now = DateTimeOffset.Now;
        var shift = StudyDay.OffsetMinutes(now.Offset, settings.DayRolloverHour);

        return cards.GetDueForecastAsync(StudyDay.StartOf(now, settings.DayRolloverHour), days, shift, ct);
    }

    public async Task<DateTimeOffset?> GetNextDueAtAsync(CancellationToken ct = default)
    {
        // Пустая очередь должна говорить «следующее повторение — завтра, 24 карточки», а не «нет данных».
        var upcoming = await cards.GetStudyCandidatesAsync(
            new StudyPoolFilter(ExcludeNew: true, Sort: StudyPoolSort.DueAsc, Limit: 1),
            ct).ConfigureAwait(false);

        return upcoming.Count == 0 ? null : upcoming[0].DueAt;
    }
}
