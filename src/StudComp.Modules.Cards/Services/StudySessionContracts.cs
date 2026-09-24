using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;

namespace StudComp.Modules.Cards.Services;

/// <summary>Запрос на старт сессии.</summary>
/// <param name="Mode">Режим (new_addons.md §5.5).</param>
/// <param name="Filter">Из чего набирать материал и как проверять.</param>
/// <param name="Seed">Зерно; <see langword="null"/> — новое случайное.</param>
public sealed record StudySessionRequest(StudyMode Mode, StudySessionFilter Filter, int? Seed = null);

/// <summary>
/// План сессии: что и в каком порядке показывать. Неизменяемый — сервис не держит состояние идущей
/// сессии, им владеет вьюмодель экрана.
/// </summary>
public sealed record StudySessionPlan(
    Guid SessionId,
    StudyMode Mode,
    StudySessionFilter Filter,
    int Seed,
    IReadOnlyList<Guid> CardIds,
    DateTimeOffset StartedAt,
    int? TimeLimitSeconds,
    int? PerCardLimitSeconds);

/// <summary>
/// Готовая к показу карточка: вопрос, ответ и всё, что нужно выбранному способу проверки.
/// </summary>
/// <param name="CheckMode">
/// <b>Фактический</b> способ проверки. Мог выродиться в самооценку, если дистракторов не хватило,
/// пропусков в тексте нет или эталон слишком длинный для ввода (new_addons.md §5.3).
/// </param>
public sealed record StudyCardView(
    Card Card,
    string QuestionText,
    string AnswerText,
    string? Hint,
    StudyCheckMode CheckMode,
    IReadOnlyList<string> Options,
    int CorrectOptionIndex,
    ClozeText? Cloze,
    IReadOnlyList<CardTag> Tags,
    string? SubjectName,
    string? SubjectColorHex,
    IReadOnlyList<ReviewPreview> Previews);

/// <summary>Ответ пользователя на карточку.</summary>
/// <param name="WasCorrect">
/// Итог автопроверки; <see langword="null"/> при самооценке — там «верно» определяет сам пользователь.
/// </param>
public sealed record SubmitAnswerRequest(
    Guid SessionId,
    Guid CardId,
    ReviewGrade Grade,
    int ElapsedMs,
    bool? WasCorrect = null);

/// <summary>Результат ответа: что назначено карточке и двинулось ли расписание вообще.</summary>
public sealed record ReviewApplied(ReviewOutcome Outcome, bool AffectedScheduling);

/// <summary>Точность по одному предмету в итогах сессии.</summary>
public sealed record SubjectScore(Guid? SubjectId, string SubjectName, int Answered, int Correct);

/// <summary>Точность по одной колоде в итогах сессии.</summary>
public sealed record DeckScore(Guid? DeckId, string DeckName, int Answered, int Correct);

/// <summary>Точность по одной метке — строка «слабые места» в итогах.</summary>
public sealed record TagScore(Guid TagId, string TagName, int Answered, int Correct);

/// <summary>Промах — строка списка ошибок с раскрытием правильного ответа.</summary>
public sealed record MissedCard(Guid CardId, string Front, string Back, Guid? SubjectId);

/// <summary>Итоги сессии — отдельный экран, а не тост (new_addons.md §5.6).</summary>
public sealed record StudySessionSummary(
    Guid SessionId,
    StudyMode Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int Planned,
    int Answered,
    int Correct,
    TimeSpan Elapsed,
    IReadOnlyList<SubjectScore> BySubject,
    IReadOnlyList<DeckScore> ByDeck,
    IReadOnlyList<TagScore> WeakTags,
    IReadOnlyList<MissedCard> Missed)
{
    /// <summary>Сколько карточек осталось без ответа: закрытая посередине сессия или пропуски экзамена.</summary>
    public int Skipped => Math.Max(0, Planned - Answered);

    /// <summary>Доля верных ответов от отвеченных.</summary>
    public double Accuracy => Answered == 0 ? 0 : (double)Correct / Answered;
}
