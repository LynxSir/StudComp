using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Агрегаты журнала ответов: календарь активности, точность по срезам, слабые места, промахи и
/// дневные лимиты (new_addons.md §6.3, §6.5, Phase 12.8).
/// </summary>
/// <remarks>
/// Всё это считается запросом, а не в памяти: журнал растёт неограниченно, а «пять худших меток»
/// нельзя взять, не посчитав все.
/// </remarks>
public sealed class CardStatisticsQueryTests : DatabaseTestBase
{
    private static readonly DateTimeOffset Day = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    // --- календарь активности --------------------------------------------------------------------

    [Fact]
    public async Task Answers_are_grouped_into_days_with_the_correct_ones_counted()
    {
        var card = TestData.Card();
        await SeedCardsAsync(card);

        await SeedLogsAsync(
            Log(card.Id, Day, ReviewGrade.Good),
            Log(card.Id, Day.AddHours(2), ReviewGrade.Again),
            Log(card.Id, Day.AddDays(1), ReviewGrade.Easy));

        var days = await new CardReviewLogRepository(Factory)
            .GetDailyCountsAsync(Day.AddDays(-5), Day.AddDays(5), 0);

        Assert.Equal(
            [
                new DailyReviewCount(new DateOnly(2026, 9, 10), 2, 1),
                new DailyReviewCount(new DateOnly(2026, 9, 11), 1, 1),
            ],
            days);
    }

    [Fact]
    public async Task Self_assessment_counts_as_correct_from_good_upwards()
    {
        var card = TestData.Card();
        await SeedCardsAsync(card);

        await SeedLogsAsync(
            Log(card.Id, Day, ReviewGrade.Again),
            Log(card.Id, Day, ReviewGrade.Hard),
            Log(card.Id, Day, ReviewGrade.Good),
            Log(card.Id, Day, ReviewGrade.Easy));

        var day = Assert.Single(
            await new CardReviewLogRepository(Factory).GetDailyCountsAsync(Day.AddDays(-1), Day.AddDays(1), 0));

        Assert.Equal(4, day.Answers);
        Assert.Equal(2, day.Correct);
    }

    [Fact]
    public async Task Automatic_checking_overrides_the_grade_when_deciding_correctness()
    {
        var card = TestData.Card();
        await SeedCardsAsync(card);

        var wrongButGraded = Log(card.Id, Day, ReviewGrade.Good);
        wrongButGraded.WasCorrect = false;

        var rightButHarsh = Log(card.Id, Day, ReviewGrade.Again);
        rightButHarsh.WasCorrect = true;

        await SeedLogsAsync(wrongButGraded, rightButHarsh);

        var day = Assert.Single(
            await new CardReviewLogRepository(Factory).GetDailyCountsAsync(Day.AddDays(-1), Day.AddDays(1), 0));

        Assert.Equal(2, day.Answers);
        Assert.Equal(1, day.Correct);
    }

    [Fact]
    public async Task The_calendar_respects_the_study_day_shift()
    {
        var card = TestData.Card();
        await SeedCardsAsync(card);
        await SeedLogsAsync(Log(card.Id, new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero), ReviewGrade.Good));

        var repository = new CardReviewLogRepository(Factory);
        var from = Day.AddDays(-5);
        var to = Day.AddDays(5);

        Assert.Equal(new DateOnly(2026, 9, 11), (await repository.GetDailyCountsAsync(from, to, 0))[0].Day);
        Assert.Equal(new DateOnly(2026, 9, 10), (await repository.GetDailyCountsAsync(from, to, -240))[0].Day);
    }

    // --- точность -------------------------------------------------------------------------------------

    [Fact]
    public async Task Accuracy_is_grouped_by_subject_including_cards_without_one()
    {
        var math = TestData.Subject("Матан");
        var loose = TestData.Card();
        var mathCard = TestData.Card(math.Id);

        await SeedCardsAsync([math], mathCard, loose);
        await SeedLogsAsync(
            Log(mathCard.Id, Day, ReviewGrade.Good),
            Log(mathCard.Id, Day, ReviewGrade.Again),
            Log(loose.Id, Day, ReviewGrade.Good));

        var rows = await new CardReviewLogRepository(Factory).GetAccuracyBySubjectAsync(Day.AddDays(-1));

        Assert.Equal(2, rows.Count);
        Assert.Equal(new ReviewAccuracyRow(math.Id, 2, 1), rows.Single(r => r.Id == math.Id));
        Assert.Equal(new ReviewAccuracyRow(null, 1, 1), rows.Single(r => r.Id is null));
    }

    [Fact]
    public async Task A_card_with_three_tags_lands_in_three_accuracy_rows()
    {
        var card = TestData.Card();
        var formulas = TestData.CardTag("формулы");
        var exam = TestData.CardTag("экзамен");
        var hard = TestData.CardTag("трудное");

        await SeedCardsAsync([], card);
        await SeedTagsAsync([formulas, exam, hard], [(card.Id, formulas.Id), (card.Id, exam.Id), (card.Id, hard.Id)]);
        await SeedLogsAsync(Log(card.Id, Day, ReviewGrade.Good));

        var rows = await new CardReviewLogRepository(Factory).GetAccuracyByTagAsync(Day.AddDays(-1), 10);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Answers));
    }

    [Fact]
    public async Task The_worst_tags_come_first_and_the_list_is_capped()
    {
        var good = TestData.Card();
        var bad = TestData.Card();
        var goodTag = TestData.CardTag("лёгкое");
        var badTag = TestData.CardTag("трудное");

        await SeedCardsAsync([], good, bad);
        await SeedTagsAsync([goodTag, badTag], [(good.Id, goodTag.Id), (bad.Id, badTag.Id)]);
        await SeedLogsAsync(
            Log(good.Id, Day, ReviewGrade.Good),
            Log(good.Id, Day, ReviewGrade.Good),
            Log(bad.Id, Day, ReviewGrade.Again),
            Log(bad.Id, Day, ReviewGrade.Again));

        var rows = await new CardReviewLogRepository(Factory).GetAccuracyByTagAsync(Day.AddDays(-1), 1);

        Assert.Equal(badTag.Id, Assert.Single(rows).Id);
    }

    // --- слабые места -------------------------------------------------------------------------------------

    [Fact]
    public async Task Weak_cards_need_a_minimum_number_of_answers_to_qualify()
    {
        var rarely = TestData.Card(front: "редкая");
        var often = TestData.Card(front: "частая");

        await SeedCardsAsync([], rarely, often);
        await SeedLogsAsync(
            Log(rarely.Id, Day, ReviewGrade.Again),
            Log(often.Id, Day, ReviewGrade.Again),
            Log(often.Id, Day, ReviewGrade.Again),
            Log(often.Id, Day, ReviewGrade.Good));

        var rows = await new CardReviewLogRepository(Factory).GetWeakCardsAsync(Day.AddDays(-1), 3, 10);

        // Одна ошибка на одном показе — ещё не «слабое место», это случайность.
        Assert.Equal(often.Id, Assert.Single(rows).Id);
    }

    [Fact]
    public async Task Weak_cards_from_the_trash_are_not_offered()
    {
        var trashed = TestData.Card();
        trashed.DeletedAt = Day;

        await SeedCardsAsync([], trashed);
        await SeedLogsAsync(Log(trashed.Id, Day, ReviewGrade.Again), Log(trashed.Id, Day, ReviewGrade.Again));

        Assert.Empty(await new CardReviewLogRepository(Factory).GetWeakCardsAsync(Day.AddDays(-1), 1, 10));
    }

    // --- промахи -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Misses_can_be_taken_from_one_session_only()
    {
        var missed = TestData.Card(front: "промах");
        var passed = TestData.Card(front: "верно");
        var session = TestData.StudySession();
        var otherSession = TestData.StudySession();

        await SeedCardsAsync([], missed, passed);
        await SeedSessionsAsync(session, otherSession);
        await SeedLogsAsync(
            Log(missed.Id, Day, ReviewGrade.Again, session.Id),
            Log(passed.Id, Day, ReviewGrade.Good, session.Id),
            Log(passed.Id, Day, ReviewGrade.Again, otherSession.Id));

        var ids = await new CardReviewLogRepository(Factory).GetMissedCardIdsAsync(Day.AddDays(-1), session.Id, 50);

        Assert.Equal([missed.Id], ids);
    }

    [Fact]
    public async Task Misses_over_a_period_are_deduplicated()
    {
        var card = TestData.Card();
        await SeedCardsAsync([], card);
        await SeedLogsAsync(
            Log(card.Id, Day, ReviewGrade.Again),
            Log(card.Id, Day.AddHours(1), ReviewGrade.Again));

        var ids = await new CardReviewLogRepository(Factory).GetMissedCardIdsAsync(Day.AddDays(-1), null, 50);

        Assert.Equal([card.Id], ids);
    }

    [Fact]
    public async Task A_miss_by_automatic_checking_counts_even_with_a_generous_grade()
    {
        var card = TestData.Card();
        await SeedCardsAsync([], card);

        var log = Log(card.Id, Day, ReviewGrade.Good);
        log.WasCorrect = false;
        await SeedLogsAsync(log);

        Assert.Equal(
            [card.Id],
            await new CardReviewLogRepository(Factory).GetMissedCardIdsAsync(Day.AddDays(-1), null, 50));
    }

    [Fact]
    public async Task Misses_of_deleted_and_suspended_cards_do_not_surface()
    {
        var trashed = TestData.Card();
        trashed.DeletedAt = Day;
        var suspended = TestData.Card();
        suspended.IsSuspended = true;

        await SeedCardsAsync([], trashed, suspended);
        await SeedLogsAsync(Log(trashed.Id, Day, ReviewGrade.Again), Log(suspended.Id, Day, ReviewGrade.Again));

        Assert.Empty(await new CardReviewLogRepository(Factory).GetMissedCardIdsAsync(Day.AddDays(-1), null, 50));
    }

    // --- дневные лимиты ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_relearning_step_is_not_mistaken_for_a_new_card()
    {
        var fresh = TestData.Card();
        var relearned = TestData.Card();
        await SeedCardsAsync([], fresh, relearned);

        var firstShow = Log(fresh.Id, Day, ReviewGrade.Good);
        firstShow.IntervalBeforeDays = 0;

        var relearn = Log(relearned.Id, Day, ReviewGrade.Good);
        relearn.IntervalBeforeDays = 10.0 / 1440.0;      // шаг «переучить» — почти ноль, но не ноль

        await SeedLogsAsync(firstShow, relearn);

        var repository = new CardReviewLogRepository(Factory);

        Assert.Equal(1, await repository.CountNewIntroducedSinceAsync(Day.AddDays(-1)));
        Assert.Equal(2, await repository.CountReviewsSinceAsync(Day.AddDays(-1)));
    }

    [Fact]
    public async Task Counters_ignore_everything_before_the_day_boundary()
    {
        var card = TestData.Card();
        await SeedCardsAsync([], card);
        await SeedLogsAsync(
            Log(card.Id, Day.AddDays(-2), ReviewGrade.Good),
            Log(card.Id, Day, ReviewGrade.Good));

        Assert.Equal(1, await new CardReviewLogRepository(Factory).CountReviewsSinceAsync(Day.AddHours(-1)));
    }

    // --- аврал ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cram_progress_counts_only_cram_answers_of_the_subject()
    {
        var math = TestData.Subject("Матан");
        var physics = TestData.Subject("Физика");
        var mathCard = TestData.Card(math.Id);
        var physicsCard = TestData.Card(physics.Id);

        await SeedCardsAsync([math, physics], mathCard, physicsCard);

        var cram = Log(mathCard.Id, Day, ReviewGrade.Good);
        cram.Mode = StudyMode.Cram;
        var earlierCram = Log(mathCard.Id, Day.AddHours(-3), ReviewGrade.Good);
        earlierCram.Mode = StudyMode.Cram;
        var practice = Log(mathCard.Id, Day, ReviewGrade.Good);
        var otherSubject = Log(physicsCard.Id, Day, ReviewGrade.Good);
        otherSubject.Mode = StudyMode.Cram;

        await SeedLogsAsync(cram, earlierCram, practice, otherSubject);

        var progress = await new CardReviewLogRepository(Factory).GetCramProgressAsync(math.Id, Day.AddDays(-30));

        Assert.Equal(2, progress.Shows);
        Assert.Equal(earlierCram.ReviewedAt, progress.FirstAt);
    }

    [Fact]
    public async Task Cram_progress_of_an_untouched_subject_is_empty()
    {
        var progress = await new CardReviewLogRepository(Factory)
            .GetCramProgressAsync(Guid.NewGuid(), Day.AddDays(-30));

        Assert.Equal(0, progress.Shows);
        Assert.Null(progress.FirstAt);
    }

    // --- нагрузка -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_year_of_history_is_aggregated_in_one_pass()
    {
        var cards = Enumerable.Range(0, 200).Select(i => TestData.Card(front: $"карточка {i}")).ToArray();
        await SeedCardsAsync([], cards);

        var logs = new List<CardReviewLog>();
        for (var day = 0; day < 365; day++)
        {
            for (var i = 0; i < 40; i++)
            {
                logs.Add(Log(cards[i % cards.Length].Id, Day.AddDays(-day).AddMinutes(i), (ReviewGrade)(i % 4)));
            }
        }

        await SeedLogsAsync([.. logs]);

        // Год журнала — это 14 600 строк, а календарю нужно 365 чисел: их считает GROUP BY, а не
        // вьюмодель. Времени тест не меряет намеренно — классы тестов идут параллельно, и секундомер
        // ловил бы соседей, а не регрессию; числа замера лежат в CURRENT.md.
        var days = await new CardReviewLogRepository(Factory)
            .GetDailyCountsAsync(Day.AddDays(-400), Day.AddDays(1), 0);

        Assert.Equal(365, days.Count);
        Assert.All(days, d => Assert.Equal(40, d.Answers));
        Assert.Equal(365 * 40, days.Sum(d => d.Answers));
    }

    // --- вспомогательное ----------------------------------------------------------------------------------------------------

    private static CardReviewLog Log(
        Guid cardId,
        DateTimeOffset reviewedAt,
        ReviewGrade grade,
        Guid? sessionId = null)
    {
        var log = TestData.CardReviewLog(cardId, sessionId);
        log.ReviewedAt = reviewedAt;
        log.Grade = grade;
        log.WasCorrect = null;
        log.IntervalBeforeDays = 1;
        return log;
    }

    private Task SeedCardsAsync(params Card[] cards) => SeedCardsAsync([], cards);

    private async Task SeedCardsAsync(Subject[] subjects, params Card[] cards)
    {
        await using var context = CreateContext();
        context.Subjects.AddRange(subjects);
        context.Cards.AddRange(cards);
        await context.SaveChangesAsync();
    }

    private async Task SeedTagsAsync(CardTag[] tags, (Guid CardId, Guid TagId)[] links)
    {
        await using var context = CreateContext();
        context.CardTags.AddRange(tags);
        context.CardTagLinks.AddRange(links.Select(l => new CardTagLink { CardId = l.CardId, TagId = l.TagId }));
        await context.SaveChangesAsync();
    }

    private async Task SeedSessionsAsync(params StudySession[] sessions)
    {
        await using var context = CreateContext();
        context.StudySessions.AddRange(sessions);
        await context.SaveChangesAsync();
    }

    private async Task SeedLogsAsync(params CardReviewLog[] logs)
    {
        await using var context = CreateContext();
        context.CardReviewLogs.AddRange(logs);
        await context.SaveChangesAsync();
    }
}
