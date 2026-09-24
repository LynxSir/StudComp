using StudComp.Core.Abstractions.Organizer;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Достаёт нужную keyed-стратегию прогноза по её ключу (<see cref="IGradeForecastStrategy.Name"/>),
/// с откатом на стратегию по умолчанию, если ключ пустой или неизвестен. Отдельный сервис нужен
/// потому, что обычный <c>IEnumerable&lt;T&gt;</c> keyed-регистрации не видит (ARCHITECTURE §9.4).
/// </summary>
internal interface IGradeForecastStrategyResolver
{
    /// <summary>Стратегия под ключом <paramref name="name"/>; пусто/неизвестно — средневзвешенная.</summary>
    IGradeForecastStrategy Resolve(string? name);
}
