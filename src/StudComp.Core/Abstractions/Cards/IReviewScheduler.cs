using StudComp.Core.Domain;

namespace StudComp.Core.Abstractions.Cards;

/// <summary>
/// Планировщик интервальных повторений (new_addons.md §6.2). Паттерн «Стратегия»: реализации
/// регистрируются keyed-сервисами и выбираются по <see cref="Card.SchedulerName"/>, по умолчанию —
/// SM-2. Зеркало <see cref="Abstractions.Organizer.IGradeForecastStrategy"/>, вплоть до резолвера
/// с откатом на стратегию по умолчанию.
/// Синхронный намеренно: чистая арифметика по уже загруженному состоянию, без I/O.
/// </summary>
public interface IReviewScheduler
{
    /// <summary>Устойчивый ключ, под которым стратегия регистрируется и хранится в карточке.</summary>
    string Name { get; }

    /// <summary>Назначить следующий срок по ответу пользователя.</summary>
    ReviewOutcome Next(ReviewState state, ReviewGrade grade, DateTimeOffset now, ReviewTuning tuning);

    /// <summary>
    /// Что будет с интервалом при каждой из четырёх оценок — подписи на кнопках. Порядок совпадает
    /// с порядком значений <see cref="ReviewGrade"/>.
    /// </summary>
    IReadOnlyList<ReviewPreview> Preview(ReviewState state, DateTimeOffset now, ReviewTuning tuning);
}
