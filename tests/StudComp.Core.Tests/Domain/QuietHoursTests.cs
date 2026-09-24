using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Тесты «тихих часов» (new_addons.md §11, Phase 12.8). Ключей в настройках эти часы ждали с 12.5,
/// но до сих пор их никто не читал — теперь читают оба планировщика напоминаний.
/// </summary>
public sealed class QuietHoursTests
{
    private static readonly TimeOnly Night = new(22, 0);
    private static readonly TimeOnly Morning = new(8, 0);

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 9, 7, hour, minute, 0, TimeSpan.FromHours(3));

    [Theory]
    [InlineData(22, true)]      // начало включительно
    [InlineData(23, true)]
    [InlineData(2, true)]       // окно переходит через полночь
    [InlineData(8, false)]      // конец исключительно
    [InlineData(12, false)]
    public void A_window_across_midnight_is_handled(int hour, bool expected) =>
        Assert.Equal(expected, QuietHours.IsQuiet(At(hour), Night, Morning));

    [Theory]
    [InlineData(22, 0, true)]
    [InlineData(7, 59, true)]
    [InlineData(8, 0, false)]
    [InlineData(21, 59, false)]
    public void A_window_across_midnight_is_handled_to_the_minute(int hour, int minute, bool expected) =>
        Assert.Equal(expected, QuietHours.IsQuiet(At(hour, minute), Night, Morning));

    [Theory]
    [InlineData(12, false)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, false)]
    public void An_ordinary_daytime_window_is_handled(int hour, bool expected) =>
        Assert.Equal(expected, QuietHours.IsQuiet(At(hour), new TimeOnly(13, 0), new TimeOnly(15, 0)));

    [Fact]
    public void An_empty_window_never_silences_anything()
    {
        // Иначе «тишина круглые сутки» молча выключила бы все напоминания и выглядела бы как поломка.
        for (var hour = 0; hour < 24; hour++)
        {
            Assert.False(QuietHours.IsQuiet(At(hour), new TimeOnly(9, 0), new TimeOnly(9, 0)));
        }
    }

    [Fact]
    public void A_moment_outside_the_window_is_allowed_as_is()
    {
        var moment = At(12);

        Assert.Equal(moment, QuietHours.NextAllowedMoment(moment, Night, Morning));
    }

    [Fact]
    public void A_late_evening_moment_is_pushed_to_the_morning()
    {
        var moment = At(23, 30);

        // Именно перенос, а не отмена: иначе напоминание в 23:00 не приходило бы никогда.
        Assert.Equal(At(8).AddDays(1), QuietHours.NextAllowedMoment(moment, Night, Morning));
    }

    [Fact]
    public void An_early_morning_moment_is_pushed_to_the_same_morning()
    {
        Assert.Equal(At(8), QuietHours.NextAllowedMoment(At(2), Night, Morning));
    }

    [Fact]
    public void A_moment_right_on_the_opening_edge_is_pushed_a_full_window()
    {
        Assert.Equal(At(8).AddDays(1), QuietHours.NextAllowedMoment(At(22), Night, Morning));
    }

    [Fact]
    public void The_pushed_moment_is_no_longer_quiet()
    {
        foreach (var hour in Enumerable.Range(0, 24))
        {
            var allowed = QuietHours.NextAllowedMoment(At(hour), Night, Morning);

            Assert.False(QuietHours.IsQuiet(allowed, Night, Morning));
        }
    }
}
