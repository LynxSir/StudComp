using Microsoft.Extensions.DependencyInjection;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Резолвит keyed-стратегии из контейнера. Неизвестный или пустой ключ — средневзвешенная
/// (<see cref="WeightedAverageForecastStrategy.Key"/>), она зарегистрирована всегда.
/// </summary>
internal sealed class GradeForecastStrategyResolver(IServiceProvider serviceProvider) : IGradeForecastStrategyResolver
{
    public IGradeForecastStrategy Resolve(string? name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? WeightedAverageForecastStrategy.Key : name;

        return serviceProvider.GetKeyedService<IGradeForecastStrategy>(key)
            ?? serviceProvider.GetRequiredKeyedService<IGradeForecastStrategy>(WeightedAverageForecastStrategy.Key);
    }
}
