using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты SM-2 (new_addons.md §6.1, Phase 12.8). Алгоритм — чистая функция, поэтому здесь
/// нет ни базы, ни таймеров: только числа на входе и числа на выходе.
/// </summary>
public sealed class SpacedRepetitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Card = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ReviewState State(
        double interval = 0,
        double ease = SpacedRepetition.DefaultEaseFactor,
        int repetitions = 0,
        int lapses = 0,
        Guid? cardId = null) =>
        new(cardId ?? Card, interval, ease, repetitions, lapses);

    private static ReviewTuning NoFuzz(double maxInterval = 365) =>
        new(MaxIntervalDays: maxInterval, FuzzRatio: 0);

    // --- лестница Good: 1 день → 6 дней → умножение на лёгкость -------------------------------

    [Theory]
    [InlineData(0, 0, 1.0)]      // новая карточка
    [InlineData(1, 1, 6.0)]      // второй успешный показ
    [InlineData(6, 2, 15.0)]     // 6 × 2.5
    [InlineData(15, 3, 37.5)]    // 15 × 2.5
    public void Good_follows_the_ladder(double interval, int repetitions, double expected)
    {
        var outcome = SpacedRepetition.Next(
            State(interval, repetitions: repetitions), ReviewGrade.Good, Now, NoFuzz());

        Assert.Equal(expected, outcome.IntervalDays, 6);
        Assert.Equal(repetitions + 1, outcome.Repetitions);
        Assert.False(outcome.IsRelearning);
    }

    [Fact]
    public void Good_keeps_ease_untouched()
    {
        var outcome = SpacedRepetition.Next(
            State(6, ease: 2.3, repetitions: 2), ReviewGrade.Good, Now, NoFuzz());

        Assert.Equal(2.3, outcome.EaseFactor, 6);
    }

    // --- Easy ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 1.3)]        // 1 × 1.3
    [InlineData(1, 1, 7.8)]        // 6 × 1.3
    [InlineData(10, 4, 32.5)]      // 10 × 2.5 × 1.3
    public void Easy_multiplies_the_ladder(double interval, int repetitions, double expected)
    {
        var outcome = SpacedRepetition.Next(
            State(interval, repetitions: repetitions), ReviewGrade.Easy, Now, NoFuzz());

        Assert.Equal(expected, outcome.IntervalDays, 6);
    }

    [Fact]
    public void Easy_raises_ease()
    {
        var outcome = SpacedRepetition.Next(
            State(10, ease: 2.5, repetitions: 4), ReviewGrade.Easy, Now, NoFuzz());

        Assert.Equal(2.65, outcome.EaseFactor, 6);
    }

    // --- Hard ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1.2)]      // новая: Max(0, 1) × 1.2
    [InlineData(10, 12.0)]
    [InlineData(100, 120.0)]
    public void Hard_stretches_the_interval_slightly(double interval, double expected)
    {
        var outcome = SpacedRepetition.Next(
            State(interval, repetitions: 3), ReviewGrade.Hard, Now, NoFuzz());

        Assert.Equal(expected, outcome.IntervalDays, 6);
    }

    [Fact]
    public void Hard_lowers_ease()
    {
        var outcome = SpacedRepetition.Next(
            State(10, ease: 2.5, repetitions: 3), ReviewGrade.Hard, Now, NoFuzz());

        Assert.Equal(2.35, outcome.EaseFactor, 6);
    }

    [Fact]
    public void Hard_keeps_counting_repetitions_and_lapses()
    {
        var outcome = SpacedRepetition.Next(
            State(10, repetitions: 3, lapses: 2), ReviewGrade.Hard, Now, NoFuzz());

        Assert.Equal(4, outcome.Repetitions);
        Assert.Equal(2, outcome.Lapses);
    }

    // --- Again: провал -------------------------------------------------------------------------

    [Fact]
    public void Again_after_a_long_interval_drops_to_relearning()
    {
        var outcome = SpacedRepetition.Next(
            State(200, ease: 2.5, repetitions: 8, lapses: 1), ReviewGrade.Again, Now, NoFuzz());

        Assert.Equal(10.0 / 1440.0, outcome.IntervalDays, 9);
        Assert.Equal(0, outcome.Repetitions);
        Assert.Equal(2, outcome.Lapses);
        Assert.Equal(2.3, outcome.EaseFactor, 6);
        Assert.True(outcome.IsRelearning);
        Assert.Equal(Now.AddMinutes(10), outcome.DueAt);
    }

    [Fact]
    public void Again_without_in_session_relearning_waits_a_day()
    {
        var tuning = new ReviewTuning(RelearnInSession: false, FuzzRatio: 0);

        var outcome = SpacedRepetition.Next(State(50, repetitions: 5), ReviewGrade.Again, Now, tuning);

        Assert.Equal(1.0, outcome.IntervalDays, 6);
        Assert.False(outcome.IsRelearning);
    }

    [Fact]
    public void Ten_minutes_then_one_day_falls_out_of_the_ladder_by_itself()
    {
        // Провал сбрасывает Repetitions, поэтому следующий успех — первая ступень лестницы.
        var failed = SpacedRepetition.Next(State(90, repetitions: 6), ReviewGrade.Again, Now, NoFuzz());

        var recovered = SpacedRepetition.Next(
            State(failed.IntervalDays, failed.EaseFactor, failed.Repetitions, failed.Lapses),
            ReviewGrade.Good,
            Now.AddMinutes(10),
            NoFuzz());

        Assert.Equal(1.0, recovered.IntervalDays, 6);
    }

    // --- пол коэффициента лёгкости --------------------------------------------------------------

    [Theory]
    [InlineData(1.35, ReviewGrade.Again)]
    [InlineData(1.4, ReviewGrade.Hard)]
    [InlineData(1.3, ReviewGrade.Again)]
    public void Ease_never_falls_below_the_floor(double ease, ReviewGrade grade)
    {
        var outcome = SpacedRepetition.Next(State(10, ease, repetitions: 3), grade, Now, NoFuzz());

        Assert.Equal(SpacedRepetition.MinEaseFactor, outcome.EaseFactor, 6);
    }

    [Fact]
    public void A_streak_of_failures_stops_at_the_floor()
    {
        var state = State(30, repetitions: 5);

        for (var i = 0; i < 20; i++)
        {
            var outcome = SpacedRepetition.Next(state, ReviewGrade.Again, Now, NoFuzz());
            state = new ReviewState(Card, outcome.IntervalDays, outcome.EaseFactor, outcome.Repetitions, outcome.Lapses);
        }

        Assert.Equal(SpacedRepetition.MinEaseFactor, state.EaseFactor, 6);
        Assert.Equal(20, state.Lapses);
    }

    // --- потолок интервала -----------------------------------------------------------------------

    [Fact]
    public void Interval_is_capped()
    {
        var outcome = SpacedRepetition.Next(
            State(300, ease: 2.5, repetitions: 9), ReviewGrade.Good, Now, NoFuzz());

        Assert.Equal(365, outcome.IntervalDays, 6);
    }

    [Fact]
    public void Fuzz_never_breaks_through_the_cap()
    {
        // Разброс применяется до потолка, поэтому потолок жёсткий, а не «365 ± 5 %».
        var tuning = new ReviewTuning(MaxIntervalDays: 365);

        for (var i = 0; i < 200; i++)
        {
            var state = State(300, repetitions: 9, cardId: Guid.NewGuid());
            var outcome = SpacedRepetition.Next(state, ReviewGrade.Easy, Now, tuning);

            Assert.True(outcome.IntervalDays <= 365);
        }
    }

    [Fact]
    public void A_custom_cap_is_honoured()
    {
        var outcome = SpacedRepetition.Next(
            State(100, repetitions: 5), ReviewGrade.Good, Now, NoFuzz(maxInterval: 30));

        Assert.Equal(30, outcome.IntervalDays, 6);
    }

    // --- разброс (fuzz) ---------------------------------------------------------------------------

    [Fact]
    public void Fuzz_is_deterministic_for_the_same_card_and_interval()
    {
        var state = State(20, repetitions: 5);
        var first = SpacedRepetition.Next(state, ReviewGrade.Good, Now, ReviewTuning.Default);

        for (var i = 0; i < 1000; i++)
        {
            var again = SpacedRepetition.Next(state, ReviewGrade.Good, Now, ReviewTuning.Default);
            Assert.Equal(first.IntervalDays, again.IntervalDays, 12);
        }
    }

    [Fact]
    public void Fuzz_stays_within_five_percent()
    {
        for (var i = 0; i < 500; i++)
        {
            var state = State(20, repetitions: 5, cardId: Guid.NewGuid());
            var outcome = SpacedRepetition.Next(state, ReviewGrade.Good, Now, ReviewTuning.Default);

            Assert.InRange(outcome.IntervalDays, 50 * 0.95, 50 * 1.05);
        }
    }

    [Theory]
    [InlineData(0, 0, ReviewGrade.Good, 1.0)]     // новая карточка: ровно день, без разброса
    [InlineData(2, 3, ReviewGrade.Hard, 2.4)]     // 2.4 дня — короче порога разброса
    public void Short_intervals_are_not_fuzzed(
        double interval, int repetitions, ReviewGrade grade, double expected)
    {
        var state = State(interval, repetitions: repetitions);

        var outcome = SpacedRepetition.Next(state, grade, Now, ReviewTuning.Default);

        Assert.Equal(expected, outcome.IntervalDays, 6);
    }

    [Fact]
    public void Different_cards_get_different_offsets()
    {
        var intervals = new HashSet<double>();

        for (var i = 0; i < 200; i++)
        {
            var state = State(20, repetitions: 5, cardId: Guid.NewGuid());
            intervals.Add(SpacedRepetition.Next(state, ReviewGrade.Good, Now, ReviewTuning.Default).IntervalDays);
        }

        // Смысл разброса именно в том, чтобы карточки одного дня разъехались.
        Assert.True(intervals.Count > 100, $"разброс дал всего {intervals.Count} различных интервалов");
    }

    // --- устойчивость к мусору ---------------------------------------------------------------------

    [Theory]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(-5, -1)]
    [InlineData(0, 0)]
    public void Broken_state_never_produces_broken_output(double interval, double ease)
    {
        foreach (var grade in Enum.GetValues<ReviewGrade>())
        {
            var outcome = SpacedRepetition.Next(
                new ReviewState(Card, interval, ease, -3, -1), grade, Now, ReviewTuning.Default);

            Assert.True(double.IsFinite(outcome.IntervalDays));
            Assert.True(outcome.IntervalDays > 0);
            Assert.True(double.IsFinite(outcome.EaseFactor));
            Assert.True(outcome.EaseFactor >= SpacedRepetition.MinEaseFactor);
            Assert.True(outcome.DueAt > Now);
        }
    }

    [Fact]
    public void Due_date_follows_the_interval()
    {
        var outcome = SpacedRepetition.Next(State(6, repetitions: 2), ReviewGrade.Good, Now, NoFuzz());

        Assert.Equal(Now.AddDays(outcome.IntervalDays), outcome.DueAt);
    }

    [Fact]
    public void Review_state_is_read_off_the_card()
    {
        var card = new Card
        {
            Id = Card,
            IntervalDays = 12,
            EaseFactor = 2.1,
            Repetitions = 4,
            Lapses = 2,
        };

        var state = ReviewState.From(card);

        Assert.Equal(new ReviewState(Card, 12, 2.1, 4, 2), state);
    }
}
