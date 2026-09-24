using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Сетка звонков семестра (new_addons.md §5): сериализация в колонку-значение и подбор слота под
/// кликнутую ячейку расписания.
/// </summary>
public class PairSlotsTests
{
    private static readonly PairSlot[] Sample =
    [
        new(1, new TimeOnly(8, 0), new TimeOnly(9, 30)),
        new(2, new TimeOnly(9, 40), new TimeOnly(11, 10)),
        new(3, new TimeOnly(11, 20), new TimeOnly(12, 50)),
    ];

    [Fact]
    public void Serialize_and_parse_round_trip()
    {
        var restored = PairSlots.Parse(PairSlots.Serialize(Sample));

        Assert.Equal(Sample.Length, restored.Count);
        Assert.Equal(Sample[0], restored[0]);
        Assert.Equal(Sample[2], restored[2]);
    }

    [Fact]
    public void Serialize_orders_slots_by_number()
    {
        var shuffled = new[] { Sample[2], Sample[0], Sample[1] };

        var restored = PairSlots.Parse(PairSlots.Serialize(shuffled));

        Assert.Equal([1, 2, 3], restored.Select(x => x.Order));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void Missing_grid_parses_as_empty(string? json)
    {
        Assert.Empty(PairSlots.Parse(json));
    }

    [Fact]
    public void Broken_json_parses_as_empty_instead_of_throwing()
    {
        // Сетка звонков не то, ради чего стоит ронять запуск приложения.
        Assert.Empty(PairSlots.Parse("{ это не массив слотов"));
    }

    [Fact]
    public void Empty_grid_serializes_to_empty_array()
    {
        Assert.Equal("[]", PairSlots.Serialize([]));
        Assert.Equal("[]", PairSlots.Serialize(null));
    }

    [Fact]
    public void Slot_for_time_inside_a_pair_returns_that_pair()
    {
        var slot = PairSlots.SlotFor(Sample, new TimeOnly(10, 0));

        Assert.Equal(2, slot!.Value.Order);
    }

    [Fact]
    public void Slot_start_is_inclusive_and_end_is_exclusive()
    {
        Assert.Equal(1, PairSlots.SlotFor(Sample, new TimeOnly(8, 0))!.Value.Order);

        // 09:30 — конец первой пары, значит уже перемена: ближайшая следующая — вторая.
        Assert.Equal(2, PairSlots.SlotFor(Sample, new TimeOnly(9, 30))!.Value.Order);
    }

    [Fact]
    public void Time_in_a_break_returns_the_next_pair()
    {
        Assert.Equal(3, PairSlots.SlotFor(Sample, new TimeOnly(11, 15))!.Value.Order);
    }

    [Fact]
    public void Time_before_the_first_pair_returns_the_first()
    {
        Assert.Equal(1, PairSlots.SlotFor(Sample, new TimeOnly(7, 0))!.Value.Order);
    }

    [Fact]
    public void Time_after_the_last_pair_returns_null()
    {
        Assert.Null(PairSlots.SlotFor(Sample, new TimeOnly(20, 0)));
    }

    [Fact]
    public void Empty_grid_never_matches()
    {
        Assert.Null(PairSlots.SlotFor([], new TimeOnly(10, 0)));
    }
}
