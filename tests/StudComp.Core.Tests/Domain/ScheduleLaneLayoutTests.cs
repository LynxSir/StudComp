using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Раскладка накладывающихся пар по дорожкам (new_addons.md §5, «показать обе недели»).
/// </summary>
public class ScheduleLaneLayoutTests
{
    private static ScheduleEntry Entry(
        DayOfWeek day, int startHour, int endHour, WeekParity parity = WeekParity.Any) => new()
        {
            Id = Guid.NewGuid(),
            SubjectId = Guid.NewGuid(),
            DayOfWeek = day,
            StartTime = new TimeOnly(startHour, 0),
            EndTime = new TimeOnly(endHour, 0),
            WeekParity = parity,
        };

    [Fact]
    public void Single_entry_takes_the_whole_width()
    {
        var entry = Entry(DayOfWeek.Monday, 9, 10);

        var lane = Assert.Single(ScheduleLaneLayout.Assign([entry]));

        Assert.Equal(entry.Id, lane.EntryId);
        Assert.Equal(0, lane.Lane);
        Assert.Equal(1, lane.LaneCount);
    }

    [Fact]
    public void Numerator_and_denominator_at_the_same_time_stand_side_by_side()
    {
        var odd = Entry(DayOfWeek.Monday, 9, 10, WeekParity.Odd);
        var even = Entry(DayOfWeek.Monday, 9, 10, WeekParity.Even);

        var lanes = ScheduleLaneLayout.Assign([odd, even]);

        Assert.Equal([0, 1], lanes.Select(x => x.Lane).Order());
        Assert.All(lanes, x => Assert.Equal(2, x.LaneCount));
    }

    [Fact]
    public void Three_overlapping_entries_get_three_lanes_of_equal_width()
    {
        var lanes = ScheduleLaneLayout.Assign(
        [
            Entry(DayOfWeek.Tuesday, 9, 12),
            Entry(DayOfWeek.Tuesday, 10, 11),
            Entry(DayOfWeek.Tuesday, 10, 13),
        ]);

        Assert.Equal([0, 1, 2], lanes.Select(x => x.Lane).Order());
        Assert.All(lanes, x => Assert.Equal(3, x.LaneCount));
    }

    [Fact]
    public void Sequential_entries_share_one_lane_and_full_width()
    {
        var lanes = ScheduleLaneLayout.Assign(
        [
            Entry(DayOfWeek.Wednesday, 9, 10),
            Entry(DayOfWeek.Wednesday, 10, 11),
            Entry(DayOfWeek.Wednesday, 11, 12),
        ]);

        Assert.All(lanes, x => Assert.Equal(0, x.Lane));
        Assert.All(lanes, x => Assert.Equal(1, x.LaneCount));
    }

    [Fact]
    public void Touching_entries_do_not_count_as_overlapping()
    {
        // Пара 09:00–10:30 и следующая 10:30–12:00 идут подряд, а не одновременно.
        var lanes = ScheduleLaneLayout.Assign(
        [
            Entry(DayOfWeek.Thursday, 9, 10),
            Entry(DayOfWeek.Thursday, 10, 11),
        ]);

        Assert.All(lanes, x => Assert.Equal(1, x.LaneCount));
    }

    [Fact]
    public void Freed_lane_is_reused_by_a_later_entry()
    {
        var lanes = ScheduleLaneLayout.Assign(
        [
            Entry(DayOfWeek.Friday, 9, 10),   // дорожка 0
            Entry(DayOfWeek.Friday, 9, 12),   // дорожка 1, тянется дольше
            Entry(DayOfWeek.Friday, 10, 11),  // дорожка 0 освободилась — переиспользуем
        ]);

        // Три пары уложились в две дорожки, потому что первая освободилась к 10:00.
        Assert.Equal(3, lanes.Count);
        Assert.Equal(2, lanes.Select(x => x.Lane).Distinct().Count());
        Assert.All(lanes, x => Assert.Equal(2, x.LaneCount));
    }

    [Fact]
    public void Different_days_are_laid_out_independently()
    {
        var lanes = ScheduleLaneLayout.Assign(
        [
            Entry(DayOfWeek.Monday, 9, 10),
            Entry(DayOfWeek.Tuesday, 9, 10),
        ]);

        Assert.All(lanes, x => Assert.Equal(0, x.Lane));
        Assert.All(lanes, x => Assert.Equal(1, x.LaneCount));
    }

    [Fact]
    public void Every_entry_gets_exactly_one_lane()
    {
        var entries = new[]
        {
            Entry(DayOfWeek.Monday, 8, 9),
            Entry(DayOfWeek.Monday, 8, 10),
            Entry(DayOfWeek.Monday, 12, 14),
            Entry(DayOfWeek.Saturday, 15, 16),
        };

        var lanes = ScheduleLaneLayout.Assign(entries);

        Assert.Equal(entries.Length, lanes.Count);
        Assert.Equal(
            entries.Select(x => x.Id).Order(),
            lanes.Select(x => x.EntryId).Order());
    }

    [Fact]
    public void Empty_schedule_produces_no_lanes()
    {
        Assert.Empty(ScheduleLaneLayout.Assign([]));
    }
}
