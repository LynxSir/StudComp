using Microsoft.Extensions.DependencyInjection;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Тесты keyed-резолвинга стратегий прогноза на настоящем контейнере (ARCHITECTURE §9.4): известный
/// ключ → своя стратегия, пустой/неизвестный → стратегия по умолчанию.
/// </summary>
public sealed class GradeForecastStrategyResolverTests
{
    private static IGradeForecastStrategyResolver BuildResolver()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IGradeForecastStrategy, WeightedAverageForecastStrategy>(
            WeightedAverageForecastStrategy.Key);
        services.AddKeyedSingleton<IGradeForecastStrategy, LinearRegressionForecastStrategy>(
            LinearRegressionForecastStrategy.Key);
        services.AddSingleton<IGradeForecastStrategyResolver, GradeForecastStrategyResolver>();

        return services.BuildServiceProvider().GetRequiredService<IGradeForecastStrategyResolver>();
    }

    [Theory]
    [InlineData("weighted-average")]
    [InlineData("linear-regression")]
    public void Resolves_a_known_key_to_its_own_strategy(string key)
    {
        Assert.Equal(key, BuildResolver().Resolve(key).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-such-strategy")]
    public void Falls_back_to_the_weighted_average_for_empty_or_unknown_keys(string? key)
    {
        Assert.Equal(WeightedAverageForecastStrategy.Key, BuildResolver().Resolve(key).Name);
    }
}
