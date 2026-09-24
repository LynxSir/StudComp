using Microsoft.Extensions.DependencyInjection;
using StudComp.Core.Abstractions.Cards;
using StudComp.Modules.Cards.Services.ReviewSchedulers;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Выбирает стратегию планирования повторений по ключу из карточки (new_addons.md §6.2).
/// </summary>
internal interface IReviewSchedulerResolver
{
    /// <summary>Неизвестный или пустой ключ — стратегия по умолчанию.</summary>
    IReviewScheduler Resolve(string? name);
}

/// <summary>
/// Резолвер keyed-стратегий. Дословное зеркало <c>GradeForecastStrategyResolver</c> из Органайзера
/// (Phase 7/11) — включая причину, по которой отдельный резолвер вообще нужен: обычный
/// <see cref="IEnumerable{T}"/> keyed-регистрации не видит.
/// </summary>
internal sealed class ReviewSchedulerResolver(IServiceProvider serviceProvider) : IReviewSchedulerResolver
{
    public IReviewScheduler Resolve(string? name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? Sm2ReviewScheduler.Key : name;

        return serviceProvider.GetKeyedService<IReviewScheduler>(key)
            ?? serviceProvider.GetRequiredKeyedService<IReviewScheduler>(Sm2ReviewScheduler.Key);
    }
}
