using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// «Аврал» — обратный отсчёт к экзамену (new_addons.md §6.4, Phase 12.8).
/// </summary>
/// <remarks>
/// Единственное место, где Картотека читает данные Органайзера. Дедлайны берутся из
/// <c>StudComp.Data</c>, поэтому ссылки между модулями не появляется.
/// </remarks>
public sealed class CramPlanServiceTests : CardsDatabaseTestBase
{
    [Fact]
    public async Task A_subject_without_an_exam_has_no_countdown()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedCardAsync("Карточка", subjectId: subjectId);

        Assert.Null(await Cram.GetStatusAsync(subjectId));
    }

    [Fact]
    public async Task A_nearby_exam_produces_an_achievable_plan()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedExamAsync(subjectId, inDays: 9);

        for (var i = 0; i < 100; i++)
        {
            await SeedCardAsync($"Карточка {i}", subjectId: subjectId);
        }

        var status = await Cram.GetStatusAsync(subjectId);

        Assert.NotNull(status);
        Assert.Equal(9, status!.DaysLeft);
        Assert.Equal(100, status.CardCount);
        Assert.Equal(3, status.RequestedShows);
        Assert.True(status.IsAchievable);
        Assert.Equal(34, status.CardsToday);
    }

    [Fact]
    public async Task A_tight_deadline_says_how_many_shows_actually_fit()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedExamAsync(subjectId, inDays: 1);

        for (var i = 0; i < 100; i++)
        {
            await SeedCardAsync($"Карточка {i}", subjectId: subjectId);
        }

        Options.CurrentValue = new CardsOptionsBuilder().WithCramShows(3).Build();

        var status = await Cram.GetStatusAsync(subjectId);

        // Честность важнее бодрости: план прямо говорит, что трижды прогнать материал не выйдет.
        Assert.NotNull(status);
        Assert.False(status!.IsAchievable);
        Assert.True(status.AchievableShows < status.RequestedShows);
    }

    [Fact]
    public async Task A_finished_deadline_is_not_a_countdown_anymore()
    {
        var subjectId = await SeedSubjectAsync();
        var examId = await SeedExamAsync(subjectId, inDays: 5);
        await SeedCardAsync("Карточка", subjectId: subjectId);

        var deadline = await DeadlineRepo.GetByIdAsync(examId);
        deadline!.Status = DeadlineStatus.Done;
        await DeadlineRepo.UpdateAsync(deadline);

        Assert.Null(await Cram.GetStatusAsync(subjectId));
    }

    [Fact]
    public async Task Only_exam_deadlines_start_a_countdown()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedCardAsync("Карточка", subjectId: subjectId);

        await DeadlineRepo.AddAsync(new Deadline
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            Title = "Лабораторная",
            DueDate = DateTimeOffset.Now.AddDays(3),
            Type = DeadlineType.Homework,
            Status = DeadlineStatus.Pending,
        });

        Assert.Null(await Cram.GetStatusAsync(subjectId));
    }

    [Fact]
    public async Task Progress_is_counted_from_the_journal()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedExamAsync(subjectId, inDays: 5);

        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            ids.Add(await SeedCardAsync($"Карточка {i}", subjectId: subjectId));
        }

        var plan = (await Sessions.StartAsync(new StudySessionRequest(
            StudyMode.Cram,
            StudySessionFilter.Empty with { SubjectIds = [subjectId] }))).Value;

        foreach (var id in ids.Take(6))
        {
            await Sessions.SubmitAnswerAsync(new SubmitAnswerRequest(plan.SessionId, id, ReviewGrade.Good, 400));
        }

        var status = await Cram.GetStatusAsync(subjectId);

        Assert.NotNull(status);
        Assert.Equal(20, status!.ProgressPercent);      // 6 показов из 30 нужных
    }

    [Fact]
    public async Task Todays_portion_is_a_ready_list_of_cards()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedExamAsync(subjectId, inDays: 5);

        for (var i = 0; i < 20; i++)
        {
            await SeedCardAsync($"Карточка {i}", subjectId: subjectId);
        }

        var today = await Cram.GetTodayAsync(subjectId);

        Assert.True(today.IsSuccess);
        Assert.Equal(12, today.Value.Count);            // 20 × 3 показа за 5 дней
    }

    [Fact]
    public async Task Asking_to_cram_without_an_exam_is_refused()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedCardAsync("Карточка", subjectId: subjectId);

        var result = await Cram.GetTodayAsync(subjectId);

        Assert.True(result.IsFailure);
        Assert.Equal("cards.cram_no_exam", result.Error.Code);
    }

    [Fact]
    public async Task An_exam_without_cards_is_refused_rather_than_answered_with_emptiness()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedExamAsync(subjectId, inDays: 5);

        var result = await Cram.GetTodayAsync(subjectId);

        Assert.True(result.IsFailure);
        Assert.Equal("cards.session_empty", result.Error.Code);
    }

    [Fact]
    public async Task The_dashboard_gets_the_nearest_exams_of_every_subject()
    {
        var math = await SeedSubjectAsync("Матан", "МА");
        var physics = await SeedSubjectAsync("Физика", "ФИ");

        await SeedExamAsync(math, inDays: 20, "Экзамен по матану");
        await SeedExamAsync(math, inDays: 40, "Пересдача");
        await SeedExamAsync(physics, inDays: 10, "Экзамен по физике");

        await SeedCardAsync("Матан", subjectId: math);
        await SeedCardAsync("Физика", subjectId: physics);

        var statuses = await Cram.GetAllAsync();

        Assert.Equal(2, statuses.Count);
        Assert.Equal("Экзамен по физике", statuses[0].DeadlineTitle);
        Assert.Equal("Экзамен по матану", statuses[1].DeadlineTitle);
    }
}
