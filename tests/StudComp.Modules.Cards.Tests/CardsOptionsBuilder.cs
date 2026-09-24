using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Собирает <see cref="CardsOptions"/> для теста: заводские значения плюс то, что тест меняет.
/// Так в тесте видно ровно ту настройку, ради которой он написан.
/// </summary>
internal sealed class CardsOptionsBuilder
{
    private readonly CardsOptions _options = new();

    public CardsOptionsBuilder WithLimits(int newPerDay, int reviewsPerDay)
    {
        _options.NewCardsPerDay = newPerDay;
        _options.ReviewsPerDay = reviewsPerDay;
        return this;
    }

    public CardsOptionsBuilder WithNewCardShare(double share)
    {
        _options.NewCardShare = share;
        return this;
    }

    public CardsOptionsBuilder WithReviewEnabled(bool enabled)
    {
        _options.ReviewEnabled = enabled;
        return this;
    }

    public CardsOptionsBuilder WithRollover(int hour)
    {
        _options.DayRolloverHour = hour;
        return this;
    }

    public CardsOptionsBuilder WithIntervalHints(bool show)
    {
        _options.ShowNextIntervalOnButtons = show;
        return this;
    }

    public CardsOptionsBuilder WithTypedThreshold(double threshold)
    {
        _options.TypedAnswerThreshold = threshold;
        return this;
    }

    public CardsOptionsBuilder WithPracticeScheduling(bool affects)
    {
        _options.PracticeAffectsScheduling = affects;
        return this;
    }

    public CardsOptionsBuilder WithQueueOrder(StudyOrder order)
    {
        _options.QueueOrder = order;
        return this;
    }

    public CardsOptionsBuilder WithCramShows(int shows)
    {
        _options.CramMinShows = shows;
        return this;
    }

    public CardsOptionsBuilder WithReminder(bool enabled, TimeOnly? at = null)
    {
        _options.ReminderEnabled = enabled;
        if (at is { } time)
        {
            _options.ReminderTime = time;
        }

        return this;
    }

    public CardsOptions Build() => _options;
}
