using Microsoft.Extensions.DependencyInjection;
using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;
using StudComp.Modules.Cards.DependencyInjection;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Резолвер keyed-стратегий повторения (new_addons.md §6.2, Phase 12.8) на настоящем контейнере.
/// </summary>
/// <remarks>
/// Отдельный резолвер существует ровно потому, что обычный <see cref="IEnumerable{T}"/>
/// keyed-регистрации не видит — тот же урок, что был выучен в Органайзере на прогнозе оценок.
/// </remarks>
public sealed class ReviewSchedulerResolverTests
{
    private static IReviewSchedulerResolver Resolver()
    {
        var services = new ServiceCollection();

        services.AddKeyedSingleton<IReviewScheduler, TestScheduler>(TestScheduler.TestKey);
        services.AddKeyedSingleton<IReviewScheduler, Sm2ReviewSchedulerProbe>("sm2");
        services.AddSingleton<IReviewSchedulerResolver, ReviewSchedulerResolver>();

        return services.BuildServiceProvider().GetRequiredService<IReviewSchedulerResolver>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("неизвестная-стратегия")]
    public void An_empty_or_unknown_key_falls_back_to_the_default(string? key)
    {
        Assert.Equal("sm2", Resolver().Resolve(key).Name);
    }

    [Fact]
    public void A_known_key_resolves_to_its_own_strategy()
    {
        Assert.Equal(TestScheduler.TestKey, Resolver().Resolve(TestScheduler.TestKey).Name);
    }

    [Fact]
    public void The_module_registers_the_default_strategy_under_its_key()
    {
        var services = new ServiceCollection();
        services.AddCardsModule();

        var scheduler = services.BuildServiceProvider().GetKeyedService<IReviewScheduler>("sm2");

        Assert.NotNull(scheduler);
        Assert.Equal("sm2", scheduler!.Name);
    }

    /// <summary>Подставная стратегия: важен только ключ, поведение здесь ни при чём.</summary>
    private sealed class TestScheduler : IReviewScheduler
    {
        public const string TestKey = "test-scheduler";

        public string Name => TestKey;

        public ReviewOutcome Next(ReviewState state, ReviewGrade grade, DateTimeOffset now, ReviewTuning tuning) =>
            new(1, 2.5, 1, 0, now.AddDays(1), false);

        public IReadOnlyList<ReviewPreview> Preview(ReviewState state, DateTimeOffset now, ReviewTuning tuning) => [];
    }

    /// <summary>Заглушка под ключом стратегии по умолчанию — сам SM-2 здесь internal.</summary>
    private sealed class Sm2ReviewSchedulerProbe : IReviewScheduler
    {
        public string Name => "sm2";

        public ReviewOutcome Next(ReviewState state, ReviewGrade grade, DateTimeOffset now, ReviewTuning tuning) =>
            SpacedRepetition.Next(state, grade, now, tuning);

        public IReadOnlyList<ReviewPreview> Preview(ReviewState state, DateTimeOffset now, ReviewTuning tuning) => [];
    }
}
