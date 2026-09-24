using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Сессия работы с карточками (new_addons.md §5): сборка плана, подготовка карточки к показу,
/// приём ответа и итоги.
/// </summary>
/// <remarks>
/// <para>
/// Сервис <b>не держит состояние идущей сессии</b>. Он отдаёт неизменяемый
/// <see cref="StudySessionPlan"/>, а текущую позицию, таймер и порядок показа держит вьюмодель
/// экрана. Причины: сервисы модуля — синглтоны, и поле «текущая сессия» стало бы скрытой глобальной
/// переменной; надёжность и так обеспечена записью в базу на каждый ответ, а не памятью; наконец,
/// «перезапуск приложения» в тесте становится просто новым экземпляром сервиса над той же базой.
/// Прецедент — <c>ReportPipeline</c>, который тоже без состояния и отдаёт задание.
/// </para>
/// <para>
/// Строка <see cref="StudySession"/> пишется <b>при старте</b>, до показа первой карточки, — по
/// образцу <c>ReportJob</c>: прерванная сессия обязана оставить след.
/// </para>
/// </remarks>
public interface IStudySessionService
{
    /// <summary>Собрать план и завести сессию.</summary>
    Task<Result<StudySessionPlan>> StartAsync(StudySessionRequest request, CancellationToken ct = default);

    /// <summary>Подготовить карточку к показу: варианты, пропуски, подсказки интервалов.</summary>
    Task<Result<StudyCardView>> PrepareCardAsync(Guid cardId, StudySessionPlan plan, CancellationToken ct = default);

    /// <summary>Сверить введённый ответ. Синхронно: чистая логика без обращения к базе.</summary>
    AnswerMatchResult CheckTypedAnswer(StudyCardView view, string? typed);

    /// <summary>Принять ответ: журнал пишется немедленно, расписание — если режим на него влияет.</summary>
    Task<Result<ReviewApplied>> SubmitAnswerAsync(SubmitAnswerRequest request, CancellationToken ct = default);

    /// <summary>
    /// Пропустить карточку: она уходит в конец очереди и остаётся неотвеченной. Журнал при этом
    /// <b>не</b> пишется — пропуск это не ответ, и в статистику ему попадать незачем.
    /// </summary>
    Task<Result> SkipAsync(Guid sessionId, Guid cardId, CancellationToken ct = default);

    /// <summary>Закрыть сессию и собрать итоги.</summary>
    Task<Result<StudySessionSummary>> FinishAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Итоги уже закрытой сессии — вкладка «Экзамен» и «Статистика».</summary>
    Task<Result<StudySessionSummary>> GetSummaryAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Незаконченная сессия не старше суток — предложение «продолжить».</summary>
    Task<StudySessionPlan?> GetResumableAsync(CancellationToken ct = default);

    /// <summary>Работа над ошибками: по конкретной сессии либо за период.</summary>
    Task<Result<StudySessionPlan>> StartMistakesAsync(
        Guid? sessionId,
        TimeSpan? period = null,
        CancellationToken ct = default);

    /// <summary>«Ещё раз то же самое»: тот же фильтр и то же зерно, новая строка сессии.</summary>
    Task<Result<StudySessionPlan>> RepeatAsync(Guid sessionId, CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class StudySessionService(
    ICardRepository cards,
    ICardDeckRepository decks,
    ICardTagService tags,
    ICardReviewLogRepository logs,
    IStudySessionRepository sessions,
    ISubjectRepository subjects,
    IActivityRepository activity,
    IReviewSchedulerResolver schedulers,
    IOptionsMonitor<CardsOptions> options) : IStudySessionService
{
    /// <summary>Из скольких чужих оборотов набираются варианты для теста.</summary>
    private const int DistractorPoolSize = 60;

    /// <summary>Сколько времени прерванная сессия остаётся «продолжаемой».</summary>
    private static readonly TimeSpan ResumeWindow = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions FilterJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<StudySessionPlan>> StartAsync(
        StudySessionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.CurrentValue;
        var filter = request.Filter ?? StudySessionFilter.Empty;
        var seed = request.Seed ?? Random.Shared.Next(1, int.MaxValue);

        var pool = await cards.GetStudyCandidatesAsync(ToPoolFilter(filter, request.Mode), ct)
            .ConfigureAwait(false);

        if (pool.Count == 0)
        {
            return Result<StudySessionPlan>.Failure(
                "cards.session_empty",
                "По выбранным условиям не нашлось ни одной карточки.");
        }

        var order = StudySessionPlanner.Build(
            pool,
            new StudyPlanOptions(
                Order: filter.Order,
                MaxCount: filter.MaxCards,
                SpreadDecks: true),
            seed);

        var session = new StudySession
        {
            Id = Guid.NewGuid(),
            Mode = request.Mode,
            StartedAt = DateTimeOffset.Now,
            SubjectId = filter.SubjectIds.Count == 1 ? filter.SubjectIds[0] : null,
            DeckId = filter.DeckIds.Count == 1 ? filter.DeckIds[0] : null,
            FilterJson = JsonSerializer.Serialize(filter, FilterJson),
            Seed = seed,
            PlannedCount = order.Count,
            TimeLimitSeconds = filter.TimeLimitSeconds,
        };

        // Пишем до показа первой карточки: иначе прерванная сессия не оставит следа.
        await sessions.AddAsync(session, ct).ConfigureAwait(false);

        return Result<StudySessionPlan>.Success(new StudySessionPlan(
            session.Id,
            request.Mode,
            filter,
            seed,
            order,
            session.StartedAt,
            filter.TimeLimitSeconds,
            filter.PerCardLimitSeconds));
    }

    public async Task<Result<StudyCardView>> PrepareCardAsync(
        Guid cardId,
        StudySessionPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var card = await cards.GetByIdAsync(cardId, ct).ConfigureAwait(false);
        if (card is null || card.DeletedAt is not null)
        {
            return Result<StudyCardView>.Failure("cards.card_not_found", "Карточка не найдена.");
        }

        var settings = options.CurrentValue;
        var filter = plan.Filter;

        var question = filter.ReverseSides ? card.Back : card.Front;
        var answer = filter.ReverseSides ? card.Front : card.Back;

        var cloze = ClozeParser.Parse(answer);
        var plainAnswer = cloze.HasGaps ? cloze.ToPlainText() : answer;

        var mode = await ResolveCheckModeAsync(card, filter, cloze, plainAnswer, plan.Seed, ct)
            .ConfigureAwait(false);

        var options4 = mode.Options;
        var subject = card.SubjectId is { } subjectId
            ? await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false)
            : null;

        var tagMap = await tags.GetForCardsAsync([card.Id], ct).ConfigureAwait(false);
        var cardTags = tagMap.TryGetValue(card.Id, out var found) ? found : [];

        var previews = settings.ShowNextIntervalOnButtons
            ? schedulers.Resolve(card.SchedulerName).Preview(ReviewState.From(card), DateTimeOffset.Now, ToTuning(settings))
            : [];

        return Result<StudyCardView>.Success(new StudyCardView(
            card,
            question,
            answer,
            // Экзамен измеряет, а не учит: подсказок в нём нет.
            plan.Mode == StudyMode.Exam ? null : card.Hint,
            mode.Mode,
            options4,
            mode.CorrectIndex,
            mode.Mode == StudyCheckMode.Cloze ? cloze : null,
            cardTags,
            subject?.Name,
            subject?.ColorHex,
            previews));
    }

    public AnswerMatchResult CheckTypedAnswer(StudyCardView view, string? typed)
    {
        ArgumentNullException.ThrowIfNull(view);

        var reference = view.Cloze is { HasGaps: true } cloze ? cloze.ToPlainText() : view.AnswerText;

        return AnswerMatching.Match(typed, reference, options.CurrentValue.TypedAnswerThreshold);
    }

    public async Task<Result<ReviewApplied>> SubmitAnswerAsync(
        SubmitAnswerRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await sessions.GetByIdAsync(request.SessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result<ReviewApplied>.Failure("cards.session_not_found", "Сессия не найдена.");
        }

        if (session.FinishedAt is not null)
        {
            return Result<ReviewApplied>.Failure("cards.session_finished", "Сессия уже закончена.");
        }

        var card = await cards.GetByIdAsync(request.CardId, ct).ConfigureAwait(false);
        if (card is null)
        {
            return Result<ReviewApplied>.Failure("cards.card_not_found", "Карточка не найдена.");
        }

        var settings = options.CurrentValue;
        var now = DateTimeOffset.Now;
        var scheduler = schedulers.Resolve(
            string.IsNullOrWhiteSpace(card.SchedulerName) ? settings.SchedulerName : card.SchedulerName);

        var state = ReviewState.From(card);
        var outcome = scheduler.Next(state, request.Grade, now, ToTuning(settings));
        var affects = AffectsScheduling(session.Mode, settings);

        // Порядок записей важен. Сначала журнал: если процесс умрёт следующей строкой, потеряется
        // обновление расписания (карточка просто всплывёт снова), а не сам ответ — его не восстановить.
        await logs.AddAsync(
            new CardReviewLog
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                SessionId = session.Id,
                ReviewedAt = now,
                Grade = request.Grade,
                Mode = session.Mode,
                ElapsedMs = Math.Max(0, request.ElapsedMs),
                IntervalBeforeDays = state.IntervalDays,
                IntervalAfterDays = affects ? outcome.IntervalDays : state.IntervalDays,
                EaseBefore = state.EaseFactor,
                EaseAfter = affects ? outcome.EaseFactor : state.EaseFactor,
                WasCorrect = request.WasCorrect,
            },
            ct).ConfigureAwait(false);

        if (affects)
        {
            await cards.ApplyReviewAsync(card.Id, outcome, now, ct).ConfigureAwait(false);
        }

        var correct = IsCorrect(request.Grade, request.WasCorrect);
        await sessions.IncrementCountersAsync(session.Id, 1, correct ? 1 : 0, ct).ConfigureAwait(false);

        return Result<ReviewApplied>.Success(new ReviewApplied(outcome, affects));
    }

    public async Task<Result> SkipAsync(Guid sessionId, Guid cardId, CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(sessionId, ct).ConfigureAwait(false);

        if (session is null)
        {
            return Result.Failure("cards.session_not_found", "Сессия не найдена.");
        }

        if (session.FinishedAt is not null)
        {
            return Result.Failure("cards.session_finished", "Сессия уже закончена.");
        }

        // Ни журнала, ни счётчиков: пропущенная карточка попадёт в итоги как «осталось без ответа».
        return Result.Success();
    }

    public async Task<Result<StudySessionSummary>> FinishAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result<StudySessionSummary>.Failure("cards.session_not_found", "Сессия не найдена.");
        }

        // Идемпотентно: повторное завершение не сдвигает время и не пишет вторую запись в ленту.
        var closed = await sessions.FinishAsync(sessionId, DateTimeOffset.Now, ct).ConfigureAwait(false);

        if (closed > 0 && session.AnsweredCount > 0)
        {
            await activity.AddAsync(
                new ActivityEntry
                {
                    Id = Guid.NewGuid(),
                    Kind = ActivityKind.CardReviewed,
                    Timestamp = DateTimeOffset.Now,
                    SubjectId = session.SubjectId,
                    RefId = session.Id,
                    Title = $"Повторено карточек: {session.AnsweredCount}",
                },
                ct).ConfigureAwait(false);
        }

        return await GetSummaryAsync(sessionId, ct).ConfigureAwait(false);
    }

    public async Task<Result<StudySessionSummary>> GetSummaryAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result<StudySessionSummary>.Failure("cards.session_not_found", "Сессия не найдена.");
        }

        var entries = await logs.GetBySessionAsync(sessionId, ct).ConfigureAwait(false);
        var ids = entries.Select(e => e.CardId).Distinct().ToList();
        var answered = await cards.GetByIdsAsync(ids, ct).ConfigureAwait(false);
        var byId = answered.ToDictionary(c => c.Id);

        var allSubjects = await subjects.GetAllAsync(ct).ConfigureAwait(false);
        var subjectNames = allSubjects.ToDictionary(s => s.Id, s => s.Name);

        var allDecks = await decks.GetAllAsync(ct).ConfigureAwait(false);
        var deckNames = allDecks.ToDictionary(d => d.Id, d => d.Name);

        var tagMap = await tags.GetForCardsAsync(ids, ct).ConfigureAwait(false);

        var rows = entries
            .Select(e => new
            {
                Card = byId.GetValueOrDefault(e.CardId),
                e.CardId,
                Correct = IsCorrect(e.Grade, e.WasCorrect),
            })
            .ToList();

        var bySubject = rows
            .GroupBy(r => r.Card?.SubjectId)
            .Select(g => new SubjectScore(
                g.Key,
                g.Key is { } id && subjectNames.TryGetValue(id, out var name) ? name : "Без предмета",
                g.Count(),
                g.Count(r => r.Correct)))
            .OrderByDescending(s => s.Answered)
            .ToList();

        var byDeck = rows
            .GroupBy(r => r.Card?.DeckId)
            .Select(g => new DeckScore(
                g.Key,
                g.Key is { } id && deckNames.TryGetValue(id, out var name) ? name : "Без колоды",
                g.Count(),
                g.Count(r => r.Correct)))
            .OrderByDescending(d => d.Answered)
            .ToList();

        // «Слабые места» — пять меток с худшей точностью.
        var weakTags = rows
            .SelectMany(r => (tagMap.GetValueOrDefault(r.CardId) ?? []).Select(tag => (tag, r.Correct)))
            .GroupBy(x => x.tag.Id)
            .Select(g => new TagScore(
                g.Key,
                g.First().tag.DisplayName,
                g.Count(),
                g.Count(x => x.Correct)))
            .OrderBy(t => t.Answered == 0 ? 1 : (double)t.Correct / t.Answered)
            .ThenByDescending(t => t.Answered)
            .Take(5)
            .ToList();

        var missed = rows
            .Where(r => !r.Correct && r.Card is not null)
            .DistinctBy(r => r.CardId)
            .Select(r => new MissedCard(r.Card!.Id, r.Card.Front, r.Card.Back, r.Card.SubjectId))
            .ToList();

        var elapsed = (session.FinishedAt ?? DateTimeOffset.Now) - session.StartedAt;

        return Result<StudySessionSummary>.Success(new StudySessionSummary(
            session.Id,
            session.Mode,
            session.StartedAt,
            session.FinishedAt,
            session.PlannedCount,
            session.AnsweredCount,
            session.CorrectCount,
            elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed,
            bySubject,
            byDeck,
            weakTags,
            missed));
    }

    public async Task<StudySessionPlan?> GetResumableAsync(CancellationToken ct = default)
    {
        var session = await sessions.GetLastUnfinishedAsync(ResumeWindow, ct).ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var filter = Deserialize(session.FilterJson);

        var pool = await cards.GetStudyCandidatesAsync(ToPoolFilter(filter, session.Mode), ct)
            .ConfigureAwait(false);
        if (pool.Count == 0)
        {
            return null;
        }

        // Порядок не пересобирается заново — он чистая функция от зерна, лежащего в самой сессии.
        var order = StudySessionPlanner.Build(
            pool,
            new StudyPlanOptions(Order: filter.Order, MaxCount: filter.MaxCards, SpreadDecks: true),
            session.Seed);

        return new StudySessionPlan(
            session.Id,
            session.Mode,
            filter,
            session.Seed,
            order,
            session.StartedAt,
            session.TimeLimitSeconds,
            filter.PerCardLimitSeconds);
    }

    public async Task<Result<StudySessionPlan>> StartMistakesAsync(
        Guid? sessionId,
        TimeSpan? period = null,
        CancellationToken ct = default)
    {
        var since = sessionId is null
            ? DateTimeOffset.Now - (period ?? TimeSpan.FromDays(30))
            : DateTimeOffset.MinValue.AddYears(1);

        var missed = await logs.GetMissedCardIdsAsync(since, sessionId, 500, ct).ConfigureAwait(false);

        if (missed.Count == 0)
        {
            return Result<StudySessionPlan>.Failure(
                "cards.mistakes_empty",
                "Промахов не нашлось — работать не над чем.");
        }

        var filter = StudySessionFilter.Empty with
        {
            CardIds = missed,
            Order = StudyOrder.HardestFirst,
            AffectsScheduling = true,
        };

        return await StartAsync(new StudySessionRequest(StudyMode.Mistakes, filter), ct).ConfigureAwait(false);
    }

    public async Task<Result<StudySessionPlan>> RepeatAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result<StudySessionPlan>.Failure("cards.session_not_found", "Сессия не найдена.");
        }

        return await StartAsync(
            new StudySessionRequest(session.Mode, Deserialize(session.FilterJson), session.Seed),
            ct).ConfigureAwait(false);
    }

    // ---- вспомогательное ---------------------------------------------------------------------

    /// <summary>Как режим относится к расписанию повторений (new_addons.md §5.5).</summary>
    private static bool AffectsScheduling(StudyMode mode, CardsOptions settings) => mode switch
    {
        StudyMode.Review or StudyMode.Mistakes => true,
        StudyMode.Exam => false,                       // экзамен измеряет, а не учит
        StudyMode.Practice => settings.PracticeAffectsScheduling,
        StudyMode.Cram => settings.PracticeAffectsScheduling,
        _ => false,
    };

    /// <summary>
    /// Единое определение «ответ верный»: автопроверка сказала «да» либо самооценка не ниже «Хорошо».
    /// То же выражение стоит в агрегатах статистики.
    /// </summary>
    private static bool IsCorrect(ReviewGrade grade, bool? wasCorrect) =>
        wasCorrect ?? grade >= ReviewGrade.Good;

    private static ReviewTuning ToTuning(CardsOptions settings) => new(
        MaxIntervalDays: settings.MaxIntervalDays,
        RelearnMinutes: settings.RelearnMinutes,
        RelearnInSession: settings.RelearnFailedInSameSession);

    private static StudyPoolFilter ToPoolFilter(StudySessionFilter filter, StudyMode mode) => new(
        SubjectIds: filter.SubjectIds,
        DeckIds: filter.DeckIds,
        TagIds: filter.TagIds,
        CardIds: filter.CardIds,
        Kinds: filter.Kinds,
        IncludeSuspended: filter.IncludeSuspended,
        DueBefore: mode == StudyMode.Review ? DateTimeOffset.Now : null,
        Sort: mode == StudyMode.Review ? StudyPoolSort.DueAsc : StudyPoolSort.None,
        Limit: 0);

    private static StudySessionFilter Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return StudySessionFilter.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<StudySessionFilter>(json, FilterJson) ?? StudySessionFilter.Empty;
        }
        catch (JsonException)
        {
            // Снимок фильтра — вспомогательные данные; битый JSON не повод ронять «повторить сессию».
            return StudySessionFilter.Empty;
        }
    }

    /// <summary>
    /// Какой способ проверки применим к этой карточке. Если выбранный не подходит — молча
    /// вырождается в самооценку: она работает всегда (new_addons.md §5.3).
    /// </summary>
    private async Task<(StudyCheckMode Mode, IReadOnlyList<string> Options, int CorrectIndex)> ResolveCheckModeAsync(
        Card card,
        StudySessionFilter filter,
        ClozeText cloze,
        string plainAnswer,
        int seed,
        CancellationToken ct)
    {
        switch (filter.CheckMode)
        {
            case StudyCheckMode.Cloze when cloze.HasGaps && !filter.ReverseSides:
                return (StudyCheckMode.Cloze, [], -1);

            case StudyCheckMode.TypedAnswer when AnswerMatching.IsTypingModeAvailable(plainAnswer):
                return (StudyCheckMode.TypedAnswer, [], -1);

            case StudyCheckMode.MultipleChoice:
                var candidates = await BuildDistractorPoolAsync(card, ct).ConfigureAwait(false);
                var set = DistractorPicker.Pick(plainAnswer, candidates, seed ^ card.Id.GetHashCode());

                return set.IsDegraded
                    ? (StudyCheckMode.SelfAssessment, [], -1)
                    : (StudyCheckMode.MultipleChoice, set.Options, set.CorrectIndex);

            default:
                return (StudyCheckMode.SelfAssessment, [], -1);
        }
    }

    /// <summary>Обороты чужих карточек: сначала по колоде, затем по предмету, затем какие есть.</summary>
    private async Task<IReadOnlyList<DistractorCandidate>> BuildDistractorPoolAsync(
        Card card,
        CancellationToken ct)
    {
        var pool = new List<DistractorCandidate>();

        if (card.DeckId is { } deckId)
        {
            Append(await cards.GetByDeckAsync(deckId, ct).ConfigureAwait(false), DistractorScope.Deck);
        }

        if (card.SubjectId is { } subjectId)
        {
            Append(await cards.GetBySubjectAsync(subjectId, ct).ConfigureAwait(false), DistractorScope.Subject);
        }

        if (pool.Count < DistractorPicker.RequiredDistractors)
        {
            Append(await cards.GetRecentAsync(DistractorPoolSize, ct).ConfigureAwait(false), DistractorScope.Global);
        }

        return pool;

        void Append(IReadOnlyList<Card> source, DistractorScope scope)
        {
            foreach (var other in source)
            {
                if (other.Id != card.Id && !string.IsNullOrWhiteSpace(other.Back))
                {
                    pool.Add(new DistractorCandidate(other.Id, other.Back, scope));
                }
            }
        }
    }
}
