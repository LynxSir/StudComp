namespace StudComp.Core.Domain;

/// <summary>Порядок предъявления карточек в сессии (new_addons.md §5.4).</summary>
public enum StudyOrder
{
    /// <summary>Случайный — значение по умолчанию.</summary>
    Random = 0,

    /// <summary>По порядку колод, внутри колоды — по времени создания.</summary>
    DeckOrder = 1,

    /// <summary>Сначала трудные: чаще забывал, тяжелее даётся.</summary>
    HardestFirst = 2,

    /// <summary>Давно не видел.</summary>
    LeastRecentlySeen = 3,

    /// <summary>Сначала новые.</summary>
    NewestFirst = 4,

    /// <summary>Сначала самые просроченные — порядок очереди дня (new_addons.md §6.3).</summary>
    DueFirst = 5,
}

/// <summary>
/// Лёгкая проекция карточки для планировщика. Ни <c>Front</c>, ни <c>Back</c> здесь нет намеренно:
/// упорядочивать пять тысяч карточек, таская за собой их Markdown, незачем.
/// </summary>
public sealed record StudyCandidate(
    Guid CardId,
    Guid? DeckId,
    Guid? SubjectId,
    int DeckSortOrder,
    int Lapses,
    double EaseFactor,
    DateTimeOffset? LastReviewedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>Новая карточка — та, которую ещё ни разу не показывали.</summary>
    public bool IsNew => DueAt is null;
}

/// <summary>Параметры сборки порядка.</summary>
/// <param name="Order">Как упорядочить.</param>
/// <param name="MaxCount">Сколько карточек взять; <c>0</c> — все.</param>
/// <param name="SpreadDecks">Не ставить рядом две карточки одной колоды, если пул это позволяет.</param>
/// <param name="NewCardShare">Доля новых карточек, вмешиваемых в поток; <c>0</c> — общий порядок.</param>
public sealed record StudyPlanOptions(
    StudyOrder Order = StudyOrder.Random,
    int MaxCount = 0,
    bool SpreadDecks = true,
    double NewCardShare = 0);

/// <summary>
/// Собирает порядок карточек сессии (new_addons.md §5.4). Чистая функция от зерна: одна и та же
/// сессия воспроизводится в точности, порядок не пересобирается при паузе, а тест может пинить
/// конкретную выдачу.
/// </summary>
public static class StudySessionPlanner
{
    /// <summary>
    /// Построить порядок предъявления.
    /// </summary>
    /// <remarks>
    /// Шаги идут строго так: снять дубли, отделить новые (если задана доля), упорядочить, обрезать
    /// до лимита, развести колоды, вмешать новые. Перестановка шагов меняет выдачу, поэтому порядок
    /// закреплён тестом.
    /// </remarks>
    public static IReadOnlyList<Guid> Build(
        IReadOnlyList<StudyCandidate> cards,
        StudyPlanOptions options,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(options);

        var unique = Deduplicate(cards);
        if (unique.Count == 0)
        {
            return [];
        }

        var rng = new Rng(seed);

        if (options.NewCardShare <= 0)
        {
            var single = Truncate(Order(unique, options.Order, ref rng), options.MaxCount);
            return ToIds(Spread(single, options));
        }

        var fresh = unique.Where(c => c.IsNew).ToList();
        var mature = unique.Where(c => !c.IsNew).ToList();

        var total = options.MaxCount > 0 ? Math.Min(options.MaxCount, unique.Count) : unique.Count;
        var freshQuota = Math.Min(fresh.Count, (int)Math.Round(total * Math.Clamp(options.NewCardShare, 0, 1)));
        var matureQuota = Math.Min(mature.Count, total - freshQuota);

        // Новых меньше квоты — добираем повторениями, а не оставляем сессию короче обещанного.
        if (freshQuota + matureQuota < total)
        {
            freshQuota = Math.Min(fresh.Count, total - matureQuota);
        }

        var orderedMature = Spread(Truncate(Order(mature, options.Order, ref rng), matureQuota), options);
        var orderedFresh = Truncate(Order(fresh, options.Order, ref rng), freshQuota);

        return ToIds(Interleave(orderedMature, orderedFresh));
    }

    private static List<StudyCandidate> Deduplicate(IReadOnlyList<StudyCandidate> cards)
    {
        var seen = new HashSet<Guid>();
        var result = new List<StudyCandidate>(cards.Count);

        foreach (var card in cards)
        {
            if (card is not null && seen.Add(card.CardId))
            {
                result.Add(card);
            }
        }

        return result;
    }

    private static List<StudyCandidate> Order(List<StudyCandidate> cards, StudyOrder order, ref Rng rng)
    {
        // Тай-брейк по идентификатору везде: без него порядок «сначала трудные» на равных значениях
        // зависел бы от того, в каком порядке база вернула строки.
        switch (order)
        {
            case StudyOrder.Random:
                var shuffled = new List<StudyCandidate>(cards);
                for (var i = shuffled.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }

                return shuffled;

            case StudyOrder.DeckOrder:
                return [.. cards
                    .OrderBy(c => c.DeckSortOrder)
                    .ThenBy(c => c.CreatedAt)
                    .ThenBy(c => c.CardId)];

            case StudyOrder.HardestFirst:
                return [.. cards
                    .OrderByDescending(c => c.Lapses)
                    .ThenBy(c => c.EaseFactor)
                    .ThenBy(c => c.CardId)];

            case StudyOrder.LeastRecentlySeen:
                return [.. cards
                    .OrderBy(c => c.LastReviewedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(c => c.CardId)];

            case StudyOrder.NewestFirst:
                return [.. cards
                    .OrderByDescending(c => c.CreatedAt)
                    .ThenBy(c => c.CardId)];

            case StudyOrder.DueFirst:
                // Новые карточки без срока уходят в конец: сначала разбираем просроченное.
                return [.. cards
                    .OrderBy(c => c.DueAt ?? DateTimeOffset.MaxValue)
                    .ThenBy(c => c.CardId)];

            default:
                return [.. cards];
        }
    }

    private static List<StudyCandidate> Truncate(List<StudyCandidate> cards, int maxCount) =>
        maxCount > 0 && cards.Count > maxCount ? cards.GetRange(0, maxCount) : cards;

    /// <summary>
    /// Развести соседей одной колоды.
    /// </summary>
    /// <remarks>
    /// На каждом шаге берётся самая «многочисленная» из оставшихся колод, отличная от предыдущей.
    /// Наивное «первый непохожий» тут не годится: оно съедает мелкие колоды первыми и оставляет
    /// хвост из одной, то есть ровно ту проблему, ради которой разведение и делается. Внутри колоды
    /// порядок, заданный шагом <c>Order</c>, сохраняется. Когда пул физически не позволяет (одна
    /// колода — почти весь материал), соседство честно остаётся: выдумывать разнообразие из ничего
    /// планировщик не должен.
    /// </remarks>
    private static List<StudyCandidate> Spread(List<StudyCandidate> cards, StudyPlanOptions options)
    {
        if (!options.SpreadDecks || options.Order == StudyOrder.DeckOrder || cards.Count < 3)
        {
            return cards;
        }

        // Список, а не словарь: колода может быть не задана (null), а колод в сессии единицы.
        var groups = new List<(Guid? Deck, Queue<StudyCandidate> Cards)>();
        foreach (var card in cards)
        {
            var index = groups.FindIndex(g => g.Deck == card.DeckId);
            if (index < 0)
            {
                groups.Add((card.DeckId, new Queue<StudyCandidate>([card])));
            }
            else
            {
                groups[index].Cards.Enqueue(card);
            }
        }

        if (groups.Count < 2)
        {
            return cards;
        }

        var result = new List<StudyCandidate>(cards.Count);
        Guid? previousDeck = null;

        while (result.Count < cards.Count)
        {
            var pick = -1;
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].Cards.Count == 0 || groups[i].Deck == previousDeck)
                {
                    continue;
                }

                if (pick < 0 || groups[i].Cards.Count > groups[pick].Cards.Count)
                {
                    pick = i;
                }
            }

            // Осталась только предыдущая колода — соседство неизбежно.
            pick = pick < 0 ? groups.FindIndex(g => g.Cards.Count > 0) : pick;

            previousDeck = groups[pick].Deck;
            result.Add(groups[pick].Cards.Dequeue());
        }

        return result;
    }

    /// <summary>Вмешать новые карточки в поток повторений равномерно, а не пачкой в начале.</summary>
    private static List<StudyCandidate> Interleave(List<StudyCandidate> mature, List<StudyCandidate> fresh)
    {
        if (fresh.Count == 0)
        {
            return mature;
        }

        if (mature.Count == 0)
        {
            return fresh;
        }

        var total = mature.Count + fresh.Count;
        var result = new List<StudyCandidate>(total);
        var step = (double)total / fresh.Count;
        var nextFresh = 0;
        var matureIndex = 0;

        for (var position = 0; position < total; position++)
        {
            var wantsFresh = nextFresh < fresh.Count && position >= (int)Math.Round(nextFresh * step);

            if (wantsFresh || matureIndex >= mature.Count)
            {
                result.Add(fresh[nextFresh++]);
            }
            else
            {
                result.Add(mature[matureIndex++]);
            }
        }

        return result;
    }

    private static IReadOnlyList<Guid> ToIds(List<StudyCandidate> cards)
    {
        var ids = new Guid[cards.Count];
        for (var i = 0; i < cards.Count; i++)
        {
            ids[i] = cards[i].CardId;
        }

        return ids;
    }

    /// <summary>
    /// Свой xorshift32 вместо <see cref="Random"/>. Зерно сессии лежит в базе годами, а «пройти ту же
    /// сессию ещё раз» обязано работать и через год, и на другой версии рантайма — значит алгоритм
    /// должен принадлежать нам и быть закреплён тестом. Смещение от остатка в <see cref="Next"/>
    /// осознанное: любая «починка» изменила бы порядок уже сохранённых зёрен.
    /// </summary>
    internal struct Rng(int seed)
    {
        private uint _state = seed == 0 ? 2463534242u : unchecked((uint)seed);

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1)
            {
                return 0;
            }

            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (int)(_state % (uint)maxExclusive);
        }
    }
}
