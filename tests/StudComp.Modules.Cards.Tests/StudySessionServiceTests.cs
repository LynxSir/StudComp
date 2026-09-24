using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Сессия тренажёра на настоящей базе (new_addons.md §5, Phase 12.8): старт, подготовка карточки,
/// приём ответа и итоги.
/// </summary>
public sealed class StudySessionServiceTests : CardsDatabaseTestBase
{
    private static StudySessionFilter Filter(
        StudyCheckMode mode = StudyCheckMode.SelfAssessment,
        bool reverse = false,
        int maxCards = 0,
        IReadOnlyList<Guid>? cardIds = null) =>
        StudySessionFilter.Empty with
        {
            CheckMode = mode,
            ReverseSides = reverse,
            MaxCards = maxCards,
            CardIds = cardIds ?? [],
            Order = StudyOrder.DeckOrder,
        };

    // --- старт -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_session_row_appears_before_the_first_card_is_shown()
    {
        await SeedCardAsync("Первая");

        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Practice, Filter()))).Value;

        var saved = await SessionRepo.GetByIdAsync(plan.SessionId);

        Assert.NotNull(saved);
        Assert.Equal(StudyMode.Practice, saved!.Mode);
        Assert.Equal(1, saved.PlannedCount);
        Assert.Null(saved.FinishedAt);
        Assert.Equal(plan.Seed, saved.Seed);
    }

    [Fact]
    public async Task An_impossible_filter_fails_instead_of_opening_an_empty_session()
    {
        var result = await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(cardIds: [Guid.NewGuid()])));

        Assert.True(result.IsFailure);
        Assert.Equal("cards.session_empty", result.Error.Code);
    }

    [Fact]
    public async Task A_limit_shortens_the_plan()
    {
        for (var i = 0; i < 10; i++)
        {
            await SeedCardAsync($"Карточка {i}");
        }

        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(maxCards: 4)))).Value;

        Assert.Equal(4, plan.CardIds.Count);
    }

    [Fact]
    public async Task Review_mode_takes_only_the_cards_whose_time_has_come()
    {
        await SeedReviewedCardAsync("Созревшая", dueInDays: -1);
        await SeedReviewedCardAsync("Ещё рано", dueInDays: 5);
        await SeedCardAsync("Новая");

        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Review, Filter()))).Value;

        var card = await CardRepo.GetByIdAsync(Assert.Single(plan.CardIds));
        Assert.Equal("Созревшая", card!.Front);
    }

    // --- подготовка карточки -------------------------------------------------------------------------

    [Fact]
    public async Task Reversing_sides_swaps_the_question_and_the_answer()
    {
        var id = await SeedCardAsync("Термин", "Определение");
        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(reverse: true)))).Value;

        var view = (await Sessions.PrepareCardAsync(id, plan)).Value;

        Assert.Equal("Определение", view.QuestionText);
        Assert.Equal("Термин", view.AnswerText);
    }

    [Fact]
    public async Task Multiple_choice_degrades_to_self_assessment_when_there_is_nobody_to_confuse_with()
    {
        var id = await SeedCardAsync("Одинокая", "Единственный ответ");
        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(StudyCheckMode.MultipleChoice)))).Value;

        var view = (await Sessions.PrepareCardAsync(id, plan)).Value;

        // Тест из двух вариантов — подбрасывание монеты, поэтому вырождаемся молча и честно.
        Assert.Equal(StudyCheckMode.SelfAssessment, view.CheckMode);
        Assert.Empty(view.Options);
    }

    [Fact]
    public async Task Multiple_choice_works_when_there_are_enough_distinct_answers()
    {
        var id = await SeedCardAsync("Предел последовательности", "Предел последовательности чисел");
        await SeedCardAsync("Матрица", "матрица перехода базиса");
        await SeedCardAsync("Интеграл", "интеграл по частям");
        await SeedCardAsync("Ряд", "ряд Тейлора в нуле");
        await SeedCardAsync("Градиент", "градиент скалярного поля");

        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(StudyCheckMode.MultipleChoice)))).Value;

        var view = (await Sessions.PrepareCardAsync(id, plan)).Value;

        Assert.Equal(StudyCheckMode.MultipleChoice, view.CheckMode);
        Assert.Equal(4, view.Options.Count);
        Assert.Equal(view.AnswerText, view.Options[view.CorrectOptionIndex]);
    }

    [Fact]
    public async Task Cloze_mode_is_offered_only_when_the_text_actually_has_gaps()
    {
        var withGaps = await SeedCardAsync("С пропусками", "Поток {{ротора}} равен циркуляции");
        var without = await SeedCardAsync("Без пропусков", "Обычный текст ответа");

        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(StudyCheckMode.Cloze)))).Value;

        var first = (await Sessions.PrepareCardAsync(withGaps, plan)).Value;
        var second = (await Sessions.PrepareCardAsync(without, plan)).Value;

        Assert.Equal(StudyCheckMode.Cloze, first.CheckMode);
        Assert.NotNull(first.Cloze);
        Assert.Equal(StudyCheckMode.SelfAssessment, second.CheckMode);
    }

    [Fact]
    public async Task Typed_answer_is_not_offered_for_a_long_reference()
    {
        var shortOne = await SeedCardAsync("Короткий", "Предел функции");
        var longOne = await SeedCardAsync("Длинный", new string('а', 200));

        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(StudyCheckMode.TypedAnswer)))).Value;

        Assert.Equal(StudyCheckMode.TypedAnswer, (await Sessions.PrepareCardAsync(shortOne, plan)).Value.CheckMode);
        Assert.Equal(StudyCheckMode.SelfAssessment, (await Sessions.PrepareCardAsync(longOne, plan)).Value.CheckMode);
    }

    [Fact]
    public async Task An_exam_hides_the_hint()
    {
        var id = await SeedCardAsync("Вопрос", "Ответ");
        var card = await CardRepo.GetByIdAsync(id);
        card!.Hint = "Подсказка";
        await CardRepo.UpdateAsync(card);

        var practice = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Practice, Filter()))).Value;
        var exam = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Exam, Filter()))).Value;

        Assert.Equal("Подсказка", (await Sessions.PrepareCardAsync(id, practice)).Value.Hint);
        Assert.Null((await Sessions.PrepareCardAsync(id, exam)).Value.Hint);
    }

    [Fact]
    public async Task Grade_buttons_carry_the_next_interval_unless_it_is_switched_off()
    {
        var id = await SeedReviewedCardAsync("С интервалом", dueInDays: -1);
        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Review, Filter()))).Value;

        Assert.Equal(4, (await Sessions.PrepareCardAsync(id, plan)).Value.Previews.Count);

        Options.CurrentValue = new CardsOptionsBuilder().WithIntervalHints(false).Build();

        Assert.Empty((await Sessions.PrepareCardAsync(id, plan)).Value.Previews);
    }

    [Fact]
    public async Task A_missing_card_is_reported_rather_than_thrown()
    {
        await SeedCardAsync("Любая");
        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Practice, Filter()))).Value;

        var result = await Sessions.PrepareCardAsync(Guid.NewGuid(), plan);

        Assert.True(result.IsFailure);
        Assert.Equal("cards.card_not_found", result.Error.Code);
    }

    // --- ввод ответа -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_typed_answer_threshold_comes_from_the_settings()
    {
        var id = await SeedCardAsync("Термин", "предел последовательности точек");
        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(StudyCheckMode.TypedAnswer)))).Value;
        var view = (await Sessions.PrepareCardAsync(id, plan)).Value;

        Options.CurrentValue = new CardsOptionsBuilder().WithTypedThreshold(0.5).Build();
        Assert.Equal(AnswerVerdict.Close, Sessions.CheckTypedAnswer(view, "предел последовательности").Verdict);

        Options.CurrentValue = new CardsOptionsBuilder().WithTypedThreshold(0.99).Build();
        Assert.Equal(AnswerVerdict.Wrong, Sessions.CheckTypedAnswer(view, "предел последовательности").Verdict);
    }

    [Fact]
    public async Task A_cloze_answer_is_compared_against_the_revealed_text()
    {
        var id = await SeedCardAsync("Термин", "Поток {{ротора}}");
        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, Filter(StudyCheckMode.Cloze)))).Value;
        var view = (await Sessions.PrepareCardAsync(id, plan)).Value;

        Assert.Equal(AnswerVerdict.Correct, Sessions.CheckTypedAnswer(view, "поток ротора").Verdict);
    }

    // --- приём ответа --------------------------------------------------------------------------------------

    [Fact]
    public async Task An_answer_is_written_to_the_journal_immediately()
    {
        // Ключевой пункт DoD: закрытое посреди сессии окно не должно стирать сделанную работу.
        var first = await SeedReviewedCardAsync("Первая", dueInDays: -1);
        var second = await SeedReviewedCardAsync("Вторая", dueInDays: -2);
        var third = await SeedReviewedCardAsync("Третья", dueInDays: -3);

        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Review, Filter()))).Value;

        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, first, ReviewGrade.Good, 1200));
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, second, ReviewGrade.Again, 3000));
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, third, ReviewGrade.Easy, 800));

        // «Перезапуск приложения»: новый экземпляр сервиса поверх той же базы.
        var afterRestart = CreateSessionService();
        var summary = (await afterRestart.GetSummaryAsync(plan.SessionId)).Value;

        Assert.Equal(3, summary.Answered);
        Assert.Equal(2, summary.Correct);
        Assert.Equal(3, (await ReviewLogRepo.GetBySessionAsync(plan.SessionId)).Count);
    }

    [Fact]
    public async Task An_interrupted_session_can_be_resumed_in_the_same_order()
    {
        await SeedReviewedCardAsync("Первая", dueInDays: -1);
        await SeedReviewedCardAsync("Вторая", dueInDays: -2);

        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Review, Filter()))).Value;

        var resumed = await CreateSessionService().GetResumableAsync();

        Assert.NotNull(resumed);
        Assert.Equal(plan.SessionId, resumed!.SessionId);
        Assert.Equal(plan.Seed, resumed.Seed);
        Assert.Equal(plan.CardIds, resumed.CardIds);
    }

    [Fact]
    public async Task Review_mode_moves_the_schedule()
    {
        var id = await SeedReviewedCardAsync("Созревшая", dueInDays: -1, intervalDays: 6, repetitions: 2);
        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Review, Filter()))).Value;

        var applied = (await Sessions.SubmitAnswerAsync(
            new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 500))).Value;

        var card = await CardRepo.GetByIdAsync(id);

        Assert.True(applied.AffectedScheduling);
        Assert.Equal(3, card!.Repetitions);
        Assert.True(card.DueAt > DateTimeOffset.Now.AddDays(10));
    }

    [Fact]
    public async Task An_exam_measures_without_teaching()
    {
        var id = await SeedReviewedCardAsync("Экзаменационная", dueInDays: -1);
        var before = await CardRepo.GetByIdAsync(id);

        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Exam, Filter()))).Value;
        var applied = (await Sessions.SubmitAnswerAsync(
            new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Again, 500, WasCorrect: false))).Value;

        var after = await CardRepo.GetByIdAsync(id);

        Assert.False(applied.AffectedScheduling);
        Assert.Equal(before!.DueAt, after!.DueAt);
        Assert.Equal(before.Repetitions, after.Repetitions);
        Assert.Equal(before.Lapses, after.Lapses);

        // Но в журнал ответ всё равно попал — иначе не было бы ни итогов, ни разбора ошибок.
        Assert.Single(await ReviewLogRepo.GetBySessionAsync(plan.SessionId));
    }

    [Fact]
    public async Task Practice_follows_the_setting()
    {
        var id = await SeedReviewedCardAsync("Тренировочная", dueInDays: -1);
        var before = await CardRepo.GetByIdAsync(id);

        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Practice, Filter()))).Value;
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 500));

        Assert.Equal(before!.DueAt, (await CardRepo.GetByIdAsync(id))!.DueAt);

        Options.CurrentValue = new CardsOptionsBuilder().WithPracticeScheduling(true).Build();

        var second = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Practice, Filter()))).Value;
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(second.SessionId, id, ReviewGrade.Good, 500));

        Assert.NotEqual(before.DueAt, (await CardRepo.GetByIdAsync(id))!.DueAt);
    }

    [Fact]
    public async Task An_answer_to_a_finished_session_is_refused()
    {
        var id = await SeedCardAsync("Любая");
        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Practice, Filter()))).Value;
        await Sessions.FinishAsync(plan.SessionId);

        var result = await Sessions.SubmitAnswerAsync(
            new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 100));

        Assert.True(result.IsFailure);
        Assert.Equal("cards.session_finished", result.Error.Code);
    }

    [Fact]
    public async Task An_answer_to_an_unknown_session_is_refused()
    {
        var id = await SeedCardAsync("Любая");

        var result = await Sessions.SubmitAnswerAsync(
            new SubmitAnswerRequest(Guid.NewGuid(), id, ReviewGrade.Good, 100));

        Assert.True(result.IsFailure);
        Assert.Equal("cards.session_not_found", result.Error.Code);
    }

    // --- завершение ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Finishing_is_idempotent_and_writes_a_single_activity_entry()
    {
        var id = await SeedCardAsync("Любая");
        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Practice, Filter()))).Value;
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 100));

        var first = (await Sessions.FinishAsync(plan.SessionId)).Value;
        var second = (await Sessions.FinishAsync(plan.SessionId)).Value;

        Assert.Equal(first.FinishedAt, second.FinishedAt);

        var entries = await ActivityRepo.GetRecentByKindsAsync(10, [ActivityKind.CardReviewed]);
        Assert.Single(entries);
    }

    [Fact]
    public async Task An_untouched_session_does_not_pollute_the_activity_feed()
    {
        await SeedCardAsync("Любая");
        var plan = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Practice, Filter()))).Value;

        await Sessions.FinishAsync(plan.SessionId);

        Assert.Empty(await ActivityRepo.GetRecentByKindsAsync(10, [ActivityKind.CardReviewed]));
    }

    [Fact]
    public async Task Summary_of_an_unknown_session_is_refused()
    {
        var result = await Sessions.GetSummaryAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("cards.session_not_found", result.Error.Code);
    }

    // --- повтор -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Repeating_a_session_reproduces_the_very_same_order()
    {
        for (var i = 0; i < 12; i++)
        {
            await SeedCardAsync($"Карточка {i}");
        }

        var filter = StudySessionFilter.Empty with { Order = StudyOrder.Random };
        var first = (await Sessions.StartAsync(new StudySessionRequest(StudyMode.Exam, filter))).Value;

        var again = (await Sessions.RepeatAsync(first.SessionId)).Value;

        Assert.NotEqual(first.SessionId, again.SessionId);
        Assert.Equal(first.Seed, again.Seed);
        Assert.Equal(first.CardIds, again.CardIds);
    }

    [Fact]
    public async Task Repeating_an_unknown_session_is_refused()
    {
        var result = await Sessions.RepeatAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("cards.session_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Working_on_mistakes_of_an_untouched_period_is_refused()
    {
        await SeedCardAsync("Любая");

        var result = await Sessions.StartMistakesAsync(null, TimeSpan.FromDays(7));

        Assert.True(result.IsFailure);
        Assert.Equal("cards.mistakes_empty", result.Error.Code);
    }
}
