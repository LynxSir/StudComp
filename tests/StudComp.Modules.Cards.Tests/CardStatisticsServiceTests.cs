using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>Статистика раздела (new_addons.md §6.5, Phase 12.8).</summary>
public sealed class CardStatisticsServiceTests : CardsDatabaseTestBase
{
    [Fact]
    public async Task The_activity_calendar_counts_answers_and_hits()
    {
        var id = await SeedCardAsync("Карточка");
        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, StudySessionFilter.Empty))).Value;

        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 100));
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Again, 100));

        var today = StudyDay.DayOf(DateTimeOffset.Now, Options.CurrentValue.DayRolloverHour);
        var calendar = await Stats.GetActivityCalendarAsync(today.AddDays(-7), today);

        var day = Assert.Single(calendar);
        Assert.Equal(2, day.Answers);
        Assert.Equal(1, day.Correct);
    }

    [Fact]
    public async Task An_untouched_calendar_is_empty_rather_than_null()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        Assert.Empty(await Stats.GetActivityCalendarAsync(today.AddDays(-30), today));
    }

    [Fact]
    public async Task A_day_of_work_starts_a_streak()
    {
        var id = await SeedCardAsync("Карточка");
        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, StudySessionFilter.Empty))).Value;
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 100));

        var streak = await Stats.GetStreakAsync();

        Assert.Equal(1, streak.Current);
        Assert.Equal(1, streak.Longest);
    }

    [Fact]
    public async Task An_empty_history_has_no_streak()
    {
        Assert.Equal(StudyStreakInfo.Empty, await Stats.GetStreakAsync());
    }

    [Fact]
    public async Task Accuracy_by_subject_is_named_for_the_ui()
    {
        var subjectId = await SeedSubjectAsync("Матан", "МА");
        var withSubject = await SeedCardAsync("С предметом", subjectId: subjectId);
        var without = await SeedCardAsync("Без предмета");

        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, StudySessionFilter.Empty))).Value;

        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, withSubject, ReviewGrade.Good, 100));
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, without, ReviewGrade.Again, 100));

        var rows = await Stats.GetAccuracyBySubjectAsync(30);

        Assert.Equal(2, rows.Count);
        Assert.Equal(1.0, rows.Single(r => r.Name == "Матан").Accuracy, 6);
        Assert.Equal(0.0, rows.Single(r => r.Name == "Без предмета").Accuracy, 6);
    }

    [Fact]
    public async Task Accuracy_by_tag_puts_the_worst_first()
    {
        var easy = await SeedCardAsync("Лёгкая", tags: ["простое"]);
        var hard = await SeedCardAsync("Трудная", tags: ["трудное"]);

        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, StudySessionFilter.Empty))).Value;

        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, easy, ReviewGrade.Good, 100));
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, hard, ReviewGrade.Again, 100));

        var rows = await Stats.GetAccuracyByTagAsync(30);

        Assert.Equal("трудное", rows[0].Name);
        Assert.Equal(0.0, rows[0].Accuracy, 6);
    }

    [Fact]
    public async Task Weak_cards_are_named_by_their_front_side()
    {
        var id = await SeedCardAsync("Теорема Стокса");
        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, StudySessionFilter.Empty))).Value;

        for (var i = 0; i < 4; i++)
        {
            await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Again, 100));
        }

        var rows = await Stats.GetWeakCardsAsync(30);

        var row = Assert.Single(rows);
        Assert.Equal("Теорема Стокса", row.Name);
        Assert.Equal(id, row.Id);
        Assert.Equal(0.0, row.Accuracy, 6);
    }

    [Fact]
    public async Task A_card_answered_once_is_not_a_weak_spot_yet()
    {
        var id = await SeedCardAsync("Случайная ошибка");
        var plan = (await Sessions.StartAsync(
            new StudySessionRequest(StudyMode.Practice, StudySessionFilter.Empty))).Value;
        await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Again, 100));

        Assert.Empty(await Stats.GetWeakCardsAsync(30));
    }

    [Fact]
    public async Task The_load_forecast_looks_at_the_coming_month()
    {
        await SeedReviewedCardAsync("Завтра", dueInDays: 1);
        await SeedReviewedCardAsync("Через месяц", dueInDays: 45);

        var forecast = await Stats.GetLoadForecastAsync(30);

        var day = Assert.Single(forecast);
        Assert.Equal(1, day.Count);
    }
}
