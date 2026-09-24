using StudComp.Core.Domain;

namespace StudComp.Modules.Organizer.Tests;

public sealed class ScheduleServiceTests : OrganizerDatabaseTestBase
{
    private static ScheduleEntry Entry(Guid subjectId, WeekParity parity = WeekParity.Any) => new()
    {
        SubjectId = subjectId,
        DayOfWeek = DayOfWeek.Monday,
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(10, 30),
        Room = "301",
        Type = ScheduleEntryType.Lecture,
        WeekParity = parity,
    };

    [Fact]
    public async Task Create_update_delete_round_trips()
    {
        var subjectId = await SeedSubjectAsync();

        var created = await Schedule.CreateAsync(Entry(subjectId));
        Assert.True(created.IsSuccess);
        Assert.Single(await Schedule.GetAllAsync());

        var edited = Entry(subjectId);
        edited.Id = created.Value;
        edited.Room = "512";
        Assert.True((await Schedule.UpdateAsync(edited)).IsSuccess);
        Assert.Equal("512", (await Schedule.GetAllAsync()).Single().Room);

        Assert.True((await Schedule.DeleteAsync(created.Value)).IsSuccess);
        Assert.Empty(await Schedule.GetAllAsync());
    }

    [Fact]
    public async Task Create_with_end_before_start_fails()
    {
        var subjectId = await SeedSubjectAsync();
        var entry = Entry(subjectId);
        entry.EndTime = new TimeOnly(8, 0);

        var result = await Schedule.CreateAsync(entry);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.schedule_time_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Create_with_unknown_subject_fails()
    {
        var result = await Schedule.CreateAsync(Entry(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetForWeek_excludes_other_parity_but_keeps_any()
    {
        var subjectId = await SeedSubjectAsync();
        await Schedule.CreateAsync(Entry(subjectId, WeekParity.Any));
        await Schedule.CreateAsync(Entry(subjectId, WeekParity.Odd));
        await Schedule.CreateAsync(Entry(subjectId, WeekParity.Even));

        var even = await Schedule.GetForWeekAsync(WeekParity.Even);

        Assert.Equal(2, even.Count);
        Assert.DoesNotContain(even, e => e.WeekParity == WeekParity.Odd);
    }
}
