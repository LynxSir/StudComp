namespace StudComp.Core.Domain;

/// <summary>Исходные данные обратного отсчёта к экзамену (new_addons.md §6.4).</summary>
/// <param name="CardCount">Сколько карточек в материале.</param>
/// <param name="DaysLeft">Сколько дней осталось до экзамена; отрицательное — экзамен уже прошёл.</param>
/// <param name="MinShowsPerCard">Сколько раз хочется показать каждую карточку.</param>
/// <param name="ShowsDone">Сколько показов уже сделано.</param>
/// <param name="MaxCardsPerDay">Потолок карточек в день; <c>0</c> — считать по остатку.</param>
public sealed record CramPlanRequest(
    int CardCount,
    int DaysLeft,
    int MinShowsPerCard,
    int ShowsDone = 0,
    int MaxCardsPerDay = 0);

/// <summary>
/// План аврала. Возвращает <b>числа и флаг</b>, а не фразу: формулировка «успеваем показать каждую
/// 2 раза вместо 3» — забота интерфейса, <c>Core</c> здесь калькулятор.
/// </summary>
public sealed record CramPlan(
    int CardsToday,
    int TotalShowsNeeded,
    int ShowsDone,
    int RequestedShowsPerCard,
    int AchievableShowsPerCard,
    bool IsAchievable,
    bool IsExpired,
    int DailyCapacity,
    int ProgressPercent);

/// <summary>
/// Раскладка материала по оставшимся до экзамена дням (new_addons.md §6.4). Чистая функция на голом
/// BCL. Главное свойство — честность: если за оставшиеся дни каждую карточку не успеть показать
/// нужное число раз, план так и говорит, а не делает вид, что всё идёт по расписанию.
/// </summary>
public static class ExamCountdownPlanner
{
    /// <summary>Посчитать план на сегодня и достижимость цели.</summary>
    public static CramPlan Plan(CramPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cards = Math.Max(0, request.CardCount);
        var shows = Math.Max(1, request.MinShowsPerCard);
        var totalNeeded = cards * shows;
        var done = Math.Clamp(request.ShowsDone, 0, totalNeeded);
        var isExpired = request.DaysLeft < 0;

        // Сегодняшний день считается за целый: «остался ноль дней» значит «всё сегодня».
        var effectiveDays = Math.Max(1, request.DaysLeft);
        var remaining = totalNeeded - done;

        // Ровная раскладка остатка по оставшимся дням.
        var spread = (int)Math.Ceiling(remaining / (double)effectiveDays);

        // Потолок дня — сколько человек физически осилит за сутки. Достижимость считается по нему,
        // а сегодняшняя порция — по ровной раскладке: незачем сваливать весь материал на сегодня
        // только потому, что теоретически он в день влезает.
        var dailyCap = request.MaxCardsPerDay > 0 ? request.MaxCardsPerDay : spread;
        var capacity = Math.Max(0, Math.Min(spread, dailyCap));

        var achievable = cards == 0
            ? shows
            : (int)Math.Floor(dailyCap * (double)effectiveDays / cards);

        return new CramPlan(
            CardsToday: isExpired ? 0 : Math.Min(capacity, remaining),
            TotalShowsNeeded: totalNeeded,
            ShowsDone: done,
            RequestedShowsPerCard: shows,
            AchievableShowsPerCard: achievable,
            IsAchievable: !isExpired && achievable >= shows,
            IsExpired: isExpired,
            DailyCapacity: capacity,
            ProgressPercent: totalNeeded == 0 ? 100 : (int)Math.Round(100.0 * done / totalNeeded));
    }

    /// <summary>
    /// Полная очередь показов: материал, повторённый <paramref name="shows"/> раз. Трудные карточки
    /// идут раньше в каждом проходе, а сами проходы сдвинуты и перемешаны — иначе второй круг был бы
    /// точной копией первого и превратился бы в зубрёжку порядка, а не материала.
    /// </summary>
    public static IReadOnlyList<Guid> BuildCycle(IReadOnlyList<StudyCandidate> cards, int shows, int seed)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var passes = Math.Max(1, shows);
        var ordered = cards
            .Where(c => c is not null)
            .DistinctBy(c => c.CardId)
            .OrderByDescending(c => c.Lapses)
            .ThenBy(c => c.EaseFactor)
            .ThenBy(c => c.LastReviewedAt ?? DateTimeOffset.MinValue)
            .ThenBy(c => c.CardId)
            .Select(c => c.CardId)
            .ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        var result = new List<Guid>(ordered.Count * passes);
        for (var pass = 0; pass < passes; pass++)
        {
            var rotation = pass * Math.Max(1, ordered.Count / passes) % ordered.Count;
            var slice = new List<Guid>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++)
            {
                slice.Add(ordered[(i + rotation) % ordered.Count]);
            }

            if (pass > 0)
            {
                var rng = new StudySessionPlanner.Rng(seed + pass);
                for (var i = slice.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (slice[i], slice[j]) = (slice[j], slice[i]);
                }
            }

            result.AddRange(slice);
        }

        return result;
    }

    /// <summary>Взять кусок очереди на сегодня, начиная с уже сделанных показов.</summary>
    public static IReadOnlyList<Guid> Slice(IReadOnlyList<Guid> cycle, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        if (count <= 0 || cycle.Count == 0)
        {
            return [];
        }

        var start = Math.Clamp(offset, 0, cycle.Count);
        var length = Math.Min(count, cycle.Count - start);
        return length <= 0 ? [] : [.. cycle.Skip(start).Take(length)];
    }
}
