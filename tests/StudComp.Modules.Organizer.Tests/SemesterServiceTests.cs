using StudComp.Core.Domain;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Семестр как сущность (new_addons.md §5): CRUD, эксклюзивность активного, кеш и разовый сид из
/// старых настроек.
/// </summary>
public sealed class SemesterServiceTests : OrganizerDatabaseTestBase
{
    private static Semester New(string name = "Осень 2026", bool active = false) => new()
    {
        Name = name,
        CourseNumber = 3,
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2026, 12, 27),
        FirstWeekIsOdd = true,
        IsActive = active,
    };

    [Fact]
    public async Task Create_read_update_delete_round_trips()
    {
        var created = await Semesters.CreateAsync(New());
        Assert.True(created.IsSuccess);

        var loaded = await Semesters.GetByIdAsync(created.Value);
        Assert.Equal("Осень 2026", loaded!.Name);
        Assert.Equal(3, loaded.CourseNumber);

        loaded.Name = "Осень 2026/27";
        Assert.True((await Semesters.UpdateAsync(loaded)).IsSuccess);
        Assert.Equal("Осень 2026/27", (await Semesters.GetByIdAsync(created.Value))!.Name);

        Assert.True((await Semesters.DeleteAsync(created.Value)).IsSuccess);
        Assert.Empty(await Semesters.GetAllAsync());
    }

    [Fact]
    public async Task First_semester_becomes_active_automatically()
    {
        var id = (await Semesters.CreateAsync(New())).Value;

        var active = await Semesters.GetActiveAsync();

        Assert.Equal(id, active!.Id);
    }

    [Fact]
    public async Task Only_one_semester_stays_active()
    {
        var first = (await Semesters.CreateAsync(New("Весна 2026"))).Value;
        var second = (await Semesters.CreateAsync(New("Осень 2026"))).Value;

        Assert.True((await Semesters.SetActiveAsync(second)).IsSuccess);

        var all = await Semesters.GetAllAsync();
        Assert.Single(all, x => x.IsActive);
        Assert.Equal(second, all.Single(x => x.IsActive).Id);
        Assert.False(all.Single(x => x.Id == first).IsActive);
    }

    [Fact]
    public async Task Active_semester_is_cached_and_refreshed_on_switch()
    {
        var first = (await Semesters.CreateAsync(New("Весна 2026"))).Value;
        Assert.Equal(first, Semesters.Current!.Id);

        var second = (await Semesters.CreateAsync(New("Осень 2026"))).Value;
        await Semesters.SetActiveAsync(second);

        Assert.Equal(second, Semesters.Current!.Id);
    }

    [Fact]
    public async Task Switching_active_semester_raises_the_event()
    {
        var raised = 0;
        Semesters.ActiveChanged += (_, _) => raised++;

        await Semesters.CreateAsync(New("Весна 2026"));
        var second = (await Semesters.CreateAsync(New("Осень 2026"))).Value;
        await Semesters.SetActiveAsync(second);

        Assert.True(raised > 0);
    }

    [Fact]
    public async Task Empty_name_is_rejected()
    {
        var semester = New();
        semester.Name = "   ";

        var result = await Semesters.CreateAsync(semester);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.semester_name_required", result.Error.Code);
    }

    [Fact]
    public async Task End_date_before_start_is_rejected()
    {
        var semester = New();
        semester.EndDate = semester.StartDate.AddDays(-1);

        var result = await Semesters.CreateAsync(semester);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.semester_dates_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Course_number_out_of_range_is_rejected()
    {
        var semester = New();
        semester.CourseNumber = 0;

        var result = await Semesters.CreateAsync(semester);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.semester_course_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Missing_semester_is_reported_not_thrown()
    {
        var ghost = New();
        ghost.Id = Guid.NewGuid();

        Assert.Equal("organizer.semester_not_found", (await Semesters.UpdateAsync(ghost)).Error.Code);
        Assert.Equal("organizer.semester_not_found", (await Semesters.DeleteAsync(ghost.Id)).Error.Code);
        Assert.Equal("organizer.semester_not_found", (await Semesters.SetActiveAsync(ghost.Id)).Error.Code);
    }

    [Fact]
    public async Task Pair_slots_are_normalized_on_save()
    {
        var semester = New();
        semester.PairSlotsJson = "не JSON вовсе";

        var id = (await Semesters.CreateAsync(semester)).Value;

        Assert.Equal("[]", (await Semesters.GetByIdAsync(id))!.PairSlotsJson);
    }

    [Fact]
    public async Task Pair_slots_round_trip_through_the_service()
    {
        var semester = New();
        semester.PairSlotsJson = PairSlots.Serialize(
            [new PairSlot(2, new TimeOnly(9, 40), new TimeOnly(11, 10)),
             new PairSlot(1, new TimeOnly(8, 0), new TimeOnly(9, 30))]);

        var id = (await Semesters.CreateAsync(semester)).Value;
        var slots = PairSlots.Parse((await Semesters.GetByIdAsync(id))!.PairSlotsJson);

        Assert.Equal([1, 2], slots.Select(x => x.Order));
    }

    [Fact]
    public async Task Seed_creates_one_active_semester_from_legacy_settings()
    {
        var result = await Semesters.SeedFromLegacyAsync(new DateOnly(2026, 2, 9), firstWeekIsOdd: false);

        Assert.True(result.IsSuccess);
        var seeded = Assert.Single(await Semesters.GetAllAsync());
        Assert.True(seeded.IsActive);
        Assert.Equal(new DateOnly(2026, 2, 9), seeded.StartDate);
        Assert.False(seeded.FirstWeekIsOdd);

        // Дату окончания придумывать нельзя — она остаётся незаданной до правки пользователем.
        Assert.Null(seeded.EndDate);
    }

    [Fact]
    public async Task Seed_is_idempotent()
    {
        await Semesters.SeedFromLegacyAsync(new DateOnly(2026, 2, 9), firstWeekIsOdd: true);
        await Semesters.SeedFromLegacyAsync(new DateOnly(2020, 1, 1), firstWeekIsOdd: false);

        var only = Assert.Single(await Semesters.GetAllAsync());
        Assert.Equal(new DateOnly(2026, 2, 9), only.StartDate);
    }

    [Fact]
    public async Task Seed_without_legacy_start_date_creates_nothing()
    {
        var result = await Semesters.SeedFromLegacyAsync(startDate: null, firstWeekIsOdd: true);

        Assert.True(result.IsSuccess);
        Assert.Empty(await Semesters.GetAllAsync());
        Assert.Null(await Semesters.GetActiveAsync());
    }

    [Fact]
    public async Task Negative_break_minutes_is_rejected()
    {
        var semester = New();
        semester.DefaultBreakMinutes = -1;

        var result = await Semesters.CreateAsync(semester);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.semester_break_invalid", result.Error.Code);
    }

    [Theory]
    [InlineData(13, 20, null, null)] // только начало задано
    [InlineData(null, null, 13, 50)] // только конец задано
    public async Task Half_set_lunch_break_is_rejected(int? startHour, int? startMinute, int? endHour, int? endMinute)
    {
        var semester = New();
        semester.LunchBreakStart = startHour is { } sh ? new TimeOnly(sh, startMinute!.Value) : null;
        semester.LunchBreakEnd = endHour is { } eh ? new TimeOnly(eh, endMinute!.Value) : null;

        var result = await Semesters.CreateAsync(semester);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.semester_lunch_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Lunch_break_end_not_after_start_is_rejected()
    {
        var semester = New();
        semester.LunchBreakStart = new TimeOnly(13, 50);
        semester.LunchBreakEnd = new TimeOnly(13, 20);

        var result = await Semesters.CreateAsync(semester);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.semester_lunch_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Break_and_lunch_fields_round_trip_through_the_service()
    {
        var semester = New();
        semester.DefaultBreakMinutes = 15;
        semester.LunchBreakStart = new TimeOnly(13, 20);
        semester.LunchBreakEnd = new TimeOnly(13, 50);

        var id = (await Semesters.CreateAsync(semester)).Value;
        var loaded = await Semesters.GetByIdAsync(id);

        Assert.Equal(15, loaded!.DefaultBreakMinutes);
        Assert.Equal(new TimeOnly(13, 20), loaded.LunchBreakStart);
        Assert.Equal(new TimeOnly(13, 50), loaded.LunchBreakEnd);
    }

    [Fact]
    public async Task Deleting_active_semester_promotes_a_remaining_one()
    {
        var first = (await Semesters.CreateAsync(New("Весна 2026"))).Value;
        var second = (await Semesters.CreateAsync(New("Осень 2026"))).Value;
        await Semesters.SetActiveAsync(second);

        await Semesters.DeleteAsync(second);

        // Флаг активного ушёл вместе с записью — сервис не должен оставлять приложение «без семестра».
        Assert.Equal(first, (await Semesters.GetActiveAsync())!.Id);
    }
}
