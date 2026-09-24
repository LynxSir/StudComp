using Microsoft.Extensions.Options;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Cards.Services;

/// <summary>Один день календаря активности.</summary>
public sealed record ActivityDay(DateOnly Day, int Answers, int Correct);

/// <summary>Точка кривой нагрузки: сколько карточек придёт на повторение в этот день.</summary>
public sealed record LoadForecastDay(DateOnly Day, int Count);

/// <summary>Строка точности по срезу — предмету, метке или карточке.</summary>
public sealed record AccuracyRow(Guid? Id, string Name, int Answers, int Correct)
{
    /// <summary>Доля верных ответов.</summary>
    public double Accuracy => Answers == 0 ? 0 : (double)Correct / Answers;
}

/// <summary>Статистика раздела (new_addons.md §6.5).</summary>
public interface ICardStatisticsService
{
    /// <summary>Календарь активности за период — сетка дней с интенсивностью.</summary>
    Task<IReadOnlyList<ActivityDay>> GetActivityCalendarAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);

    /// <summary>Серия дней подряд — одна строка, без геймификации.</summary>
    Task<StudyStreakInfo> GetStreakAsync(CancellationToken ct = default);

    /// <summary>Точность по предметам за последние N дней.</summary>
    Task<IReadOnlyList<AccuracyRow>> GetAccuracyBySubjectAsync(int days, CancellationToken ct = default);

    /// <summary>Точность по меткам за последние N дней, худшие сверху.</summary>
    Task<IReadOnlyList<AccuracyRow>> GetAccuracyByTagAsync(int days, int take = 10, CancellationToken ct = default);

    /// <summary>Слабые места: карточки с худшей точностью, кнопка «Гонять их» берёт их идентификаторы.</summary>
    Task<IReadOnlyList<AccuracyRow>> GetWeakCardsAsync(int days, int take = 20, CancellationToken ct = default);

    /// <summary>Кривая нагрузки на N дней вперёд — чтобы не устроить себе завал перед сессией.</summary>
    Task<IReadOnlyList<LoadForecastDay>> GetLoadForecastAsync(int days = 30, CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class CardStatisticsService(
    ICardRepository cards,
    ICardReviewLogRepository logs,
    ICardTagRepository tags,
    ISubjectRepository subjects,
    IOptionsMonitor<CardsOptions> options) : ICardStatisticsService
{
    /// <summary>Сколько ответов нужно, чтобы карточка вообще претендовала на «слабое место».</summary>
    private const int WeakCardMinAnswers = 3;

    public async Task<IReadOnlyList<ActivityDay>> GetActivityCalendarAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var settings = options.CurrentValue;
        var offset = DateTimeOffset.Now.Offset;
        var shift = StudyDay.OffsetMinutes(offset, settings.DayRolloverHour);

        var rows = await logs.GetDailyCountsAsync(
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), offset),
            new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), offset),
            shift,
            ct).ConfigureAwait(false);

        return [.. rows.Select(r => new ActivityDay(r.Day, r.Answers, r.Correct))];
    }

    public async Task<StudyStreakInfo> GetStreakAsync(CancellationToken ct = default)
    {
        var today = StudyDay.DayOf(DateTimeOffset.Now, options.CurrentValue.DayRolloverHour);
        var calendar = await GetActivityCalendarAsync(today.AddDays(-400), today, ct).ConfigureAwait(false);

        return StudyStreak.Calculate([.. calendar.Where(d => d.Answers > 0).Select(d => d.Day)], today);
    }

    public async Task<IReadOnlyList<AccuracyRow>> GetAccuracyBySubjectAsync(
        int days,
        CancellationToken ct = default)
    {
        var rows = await logs.GetAccuracyBySubjectAsync(Since(days), ct).ConfigureAwait(false);
        var names = (await subjects.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(s => s.Id, s => s.Name);

        return
        [
            .. rows.Select(r => new AccuracyRow(
                r.Id,
                r.Id is { } id && names.TryGetValue(id, out var name) ? name : "Без предмета",
                r.Answers,
                r.Correct)),
        ];
    }

    public async Task<IReadOnlyList<AccuracyRow>> GetAccuracyByTagAsync(
        int days,
        int take = 10,
        CancellationToken ct = default)
    {
        var rows = await logs.GetAccuracyByTagAsync(Since(days), take, ct).ConfigureAwait(false);
        var names = (await tags.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(t => t.Id, t => t.DisplayName);

        return
        [
            .. rows.Select(r => new AccuracyRow(
                r.Id,
                r.Id is { } id && names.TryGetValue(id, out var name) ? name : "метка",
                r.Answers,
                r.Correct)),
        ];
    }

    public async Task<IReadOnlyList<AccuracyRow>> GetWeakCardsAsync(
        int days,
        int take = 20,
        CancellationToken ct = default)
    {
        var rows = await logs.GetWeakCardsAsync(Since(days), WeakCardMinAnswers, take, ct).ConfigureAwait(false);
        var ids = rows.Where(r => r.Id is not null).Select(r => r.Id!.Value).ToList();
        var found = await cards.GetByIdsAsync(ids, ct).ConfigureAwait(false);
        var fronts = found.ToDictionary(c => c.Id, c => c.Front);

        return
        [
            .. rows.Select(r => new AccuracyRow(
                r.Id,
                r.Id is { } id && fronts.TryGetValue(id, out var front) ? front : "карточка",
                r.Answers,
                r.Correct)),
        ];
    }

    public async Task<IReadOnlyList<LoadForecastDay>> GetLoadForecastAsync(
        int days = 30,
        CancellationToken ct = default)
    {
        var settings = options.CurrentValue;
        var now = DateTimeOffset.Now;
        var shift = StudyDay.OffsetMinutes(now.Offset, settings.DayRolloverHour);

        var rows = await cards
            .GetDueForecastAsync(StudyDay.StartOf(now, settings.DayRolloverHour), days, shift, ct)
            .ConfigureAwait(false);

        return [.. rows.Select(r => new LoadForecastDay(r.Day, r.Count))];
    }

    private static DateTimeOffset Since(int days) => DateTimeOffset.Now.AddDays(-Math.Max(1, days));
}
