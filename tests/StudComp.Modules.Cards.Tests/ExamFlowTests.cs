using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Сквозной проход пробного экзамена и работы над ошибками (new_addons.md §5.6, Phase 12.8).
/// </summary>
/// <remarks>
/// Это тот самый пункт DoD «экзамен из 40 карточек по трём предметам проходится от начала до итогов,
/// а работа над ошибками поднимает ровно промахи» — целиком, от конструктора до второй сессии.
/// </remarks>
public sealed class ExamFlowTests : CardsDatabaseTestBase
{
    private const int PerSubject = 30;
    private const int ExamSize = 40;

    [Fact]
    public async Task An_exam_over_three_subjects_runs_from_start_to_results()
    {
        var (subjects, _) = await SeedThreeSubjectsAsync();

        var plan = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Exam,
            StudySessionFilter.Empty with
            {
                SubjectIds = subjects,
                MaxCards = ExamSize,
                Order = StudyOrder.Random,
            }))).Value;

        Assert.Equal(ExamSize, plan.CardIds.Count);
        Assert.Equal(ExamSize, plan.CardIds.Distinct().Count());

        // Три предмета в билете — иначе «случайный экзамен» выдал бы весь материал одной темы.
        var answered = await CardRepo.GetByIdsAsync(plan.CardIds);
        Assert.Equal(3, answered.Select(c => c.SubjectId).Distinct().Count());

        var missed = await AnswerAsync(plan, wrongCount: 6);

        var summary = (await Sessions.FinishAsync(plan.SessionId)).Value;

        Assert.Equal(ExamSize, summary.Planned);
        Assert.Equal(ExamSize, summary.Answered);
        Assert.Equal(34, summary.Correct);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0.85, summary.Accuracy, 6);
        Assert.NotNull(summary.FinishedAt);

        Assert.Equal(3, summary.BySubject.Count);
        Assert.Equal(ExamSize, summary.BySubject.Sum(s => s.Answered));

        Assert.Equal(
            missed.OrderBy(id => id),
            summary.Missed.Select(m => m.CardId).OrderBy(id => id));
    }

    [Fact]
    public async Task Working_on_mistakes_picks_up_exactly_the_misses()
    {
        var (subjects, _) = await SeedThreeSubjectsAsync();

        var plan = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Exam,
            StudySessionFilter.Empty with { SubjectIds = subjects, MaxCards = ExamSize }))).Value;

        var missed = await AnswerAsync(plan, wrongCount: 6);
        await Sessions.FinishAsync(plan.SessionId);

        var mistakes = (await Sessions.StartMistakesAsync(plan.SessionId)).Value;

        Assert.Equal(StudyMode.Mistakes, mistakes.Mode);
        Assert.Equal(missed.OrderBy(id => id), mistakes.CardIds.OrderBy(id => id));
    }

    [Fact]
    public async Task An_exam_does_not_move_a_single_due_date()
    {
        var (subjects, ids) = await SeedThreeSubjectsAsync(reviewed: true);
        var before = (await CardRepo.GetByIdsAsync(ids)).ToDictionary(c => c.Id, c => c.DueAt);

        var plan = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Exam,
            StudySessionFilter.Empty with { SubjectIds = subjects, MaxCards = ExamSize }))).Value;

        await AnswerAsync(plan, wrongCount: 6);
        await Sessions.FinishAsync(plan.SessionId);

        var after = await CardRepo.GetByIdsAsync(ids);

        Assert.All(after, card => Assert.Equal(before[card.Id], card.DueAt));
    }

    [Fact]
    public async Task Mistakes_of_the_exam_are_taught_rather_than_measured()
    {
        var (subjects, _) = await SeedThreeSubjectsAsync(reviewed: true);

        var exam = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Exam,
            StudySessionFilter.Empty with { SubjectIds = subjects, MaxCards = ExamSize }))).Value;

        var missed = await AnswerAsync(exam, wrongCount: 6);
        await Sessions.FinishAsync(exam.SessionId);

        var mistakes = (await Sessions.StartMistakesAsync(exam.SessionId)).Value;
        var target = mistakes.CardIds[0];
        var before = await CardRepo.GetByIdAsync(target);

        await Sessions.SubmitAnswerAsync(
            new SubmitAnswerRequest(mistakes.SessionId, target, ReviewGrade.Good, 700));

        var after = await CardRepo.GetByIdAsync(target);

        Assert.Contains(target, missed);
        Assert.NotEqual(before!.DueAt, after!.DueAt);
    }

    [Fact]
    public async Task The_results_carry_the_weakest_tags()
    {
        var subjectId = await SeedSubjectAsync();
        var easy = await SeedCardAsync("Лёгкая", subjectId: subjectId, tags: ["простое"]);
        var hard = await SeedCardAsync("Трудная", subjectId: subjectId, tags: ["трудное"]);

        var plan = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Exam,
            StudySessionFilter.Empty with { SubjectIds = [subjectId] }))).Value;

        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, easy, ReviewGrade.Good, 100, true));
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, hard, ReviewGrade.Again, 100, false));

        var summary = (await Sessions.FinishAsync(plan.SessionId)).Value;

        Assert.Equal("трудное", summary.WeakTags[0].TagName);
        Assert.Equal(0, summary.WeakTags[0].Correct);
    }

    [Fact]
    public async Task A_session_closed_halfway_reports_what_was_skipped()
    {
        var (subjects, _) = await SeedThreeSubjectsAsync();

        var plan = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Exam,
            StudySessionFilter.Empty with { SubjectIds = subjects, MaxCards = ExamSize }))).Value;

        foreach (var id in plan.CardIds.Take(10))
        {
            await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 100, true));
        }

        var summary = (await Sessions.FinishAsync(plan.SessionId)).Value;

        Assert.Equal(10, summary.Answered);
        Assert.Equal(30, summary.Skipped);
    }

    [Fact]
    public async Task Running_the_very_same_exam_again_keeps_the_ticket()
    {
        var (subjects, _) = await SeedThreeSubjectsAsync();

        var first = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Exam,
            StudySessionFilter.Empty with
            {
                SubjectIds = subjects,
                MaxCards = ExamSize,
                Order = StudyOrder.Random,
            }))).Value;

        var again = (await Sessions.RepeatAsync(first.SessionId)).Value;

        Assert.Equal(first.CardIds, again.CardIds);
    }

    [Fact]
    public async Task Results_break_down_by_deck_as_well()
    {
        var subjectId = await SeedSubjectAsync();
        var deckId = await SeedDeckAsync("Раздел 1", subjectId);

        var inDeck = await SeedCardAsync("В колоде", subjectId: subjectId, deckId: deckId);
        var loose = await SeedCardAsync("Россыпью", subjectId: subjectId);

        var plan = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Exam,
            StudySessionFilter.Empty with { SubjectIds = [subjectId] }))).Value;

        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, inDeck, ReviewGrade.Good, 100, true));
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, loose, ReviewGrade.Good, 100, true));

        var summary = (await Sessions.FinishAsync(plan.SessionId)).Value;

        Assert.Equal(2, summary.ByDeck.Count);
        Assert.Contains(summary.ByDeck, d => d.DeckName == "Раздел 1");
        Assert.Contains(summary.ByDeck, d => d.DeckName == "Без колоды");
    }

    // --- вспомогательное -----------------------------------------------------------------------------

    private async Task<(IReadOnlyList<Guid> Subjects, IReadOnlyList<Guid> Cards)> SeedThreeSubjectsAsync(
        bool reviewed = false)
    {
        var subjects = new List<Guid>();
        var cards = new List<Guid>();

        foreach (var name in (string[])["Матан", "Физика", "Дискретка"])
        {
            var subjectId = await SeedSubjectAsync(name, name[..2]);
            subjects.Add(subjectId);

            for (var i = 0; i < PerSubject; i++)
            {
                cards.Add(reviewed
                    ? await SeedReviewedCardAsync($"{name} вопрос {i}", dueInDays: -1, subjectId: subjectId)
                    : await SeedCardAsync($"{name} вопрос {i}", subjectId: subjectId));
            }
        }

        return (subjects, cards);
    }

    /// <summary>Ответить на весь билет, ошибившись ровно <paramref name="wrongCount"/> раз.</summary>
    private async Task<IReadOnlyList<Guid>> AnswerAsync(StudySessionPlan plan, int wrongCount)
    {
        var missed = new List<Guid>();

        for (var i = 0; i < plan.CardIds.Count; i++)
        {
            var wrong = i < wrongCount;
            if (wrong)
            {
                missed.Add(plan.CardIds[i]);
            }

            await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(
                plan.SessionId,
                plan.CardIds[i],
                wrong ? ReviewGrade.Again : ReviewGrade.Good,
                900,
                WasCorrect: !wrong));
        }

        return missed;
    }
}
