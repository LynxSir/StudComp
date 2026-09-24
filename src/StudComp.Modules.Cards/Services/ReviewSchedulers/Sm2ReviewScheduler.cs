using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;

namespace StudComp.Modules.Cards.Services.ReviewSchedulers;

/// <summary>
/// SM-2 — стратегия планирования повторений по умолчанию (new_addons.md §6.1, §6.2).
/// </summary>
/// <remarks>
/// Обёртка вокруг <see cref="SpacedRepetition"/>: вся арифметика живёт в <c>Core</c> и покрыта
/// таблично, стратегии остаётся только назвать себя и разложить предпросмотр по четырём оценкам.
/// </remarks>
internal sealed class Sm2ReviewScheduler : IReviewScheduler
{
    /// <summary>Ключ регистрации и значение <see cref="Card.SchedulerName"/> по умолчанию.</summary>
    public const string Key = "sm2";

    /// <inheritdoc />
    public string Name => Key;

    /// <inheritdoc />
    public ReviewOutcome Next(ReviewState state, ReviewGrade grade, DateTimeOffset now, ReviewTuning tuning) =>
        SpacedRepetition.Next(state, grade, now, tuning);

    /// <inheritdoc />
    public IReadOnlyList<ReviewPreview> Preview(ReviewState state, DateTimeOffset now, ReviewTuning tuning)
    {
        var previews = new List<ReviewPreview>(4);

        foreach (var grade in Enum.GetValues<ReviewGrade>())
        {
            var outcome = SpacedRepetition.Next(state, grade, now, tuning);
            previews.Add(new ReviewPreview(grade, outcome.IntervalDays, outcome.DueAt));
        }

        return previews;
    }
}
