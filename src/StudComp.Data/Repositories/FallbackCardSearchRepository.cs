using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>
/// Поиск без FTS5 (new_addons.md §4.3): фильтры считает база, текст сопоставляется в памяти.
/// </summary>
/// <remarks>
/// <para>
/// Включается, только если виртуальной таблицы в схеме нет — например, база восстановлена из
/// старого архива или её правили руками. Ранжирование деградирует до «совпало в лицевой стороне —
/// выше», зато поведение очевидно правильное: именно поэтому эта же реализация служит в тестах
/// эталоном того, что вообще обязано находиться.
/// </para>
/// <para>
/// Сопоставление идёт в памяти намеренно: <c>lower()</c> и <c>LIKE</c> в SQLite складывают регистр
/// только у латиницы, и «Интеграл» по запросу «интеграл» в SQL просто не нашёлся бы.
/// </para>
/// </remarks>
internal sealed class FallbackCardSearchRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), ICardSearchRepository
{
    public async Task<IReadOnlyList<CardSearchHit>> SearchAsync(
        CardQuerySpec spec,
        CardSearchOptions options,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var inTrash = spec.Flags.HasFlag(CardQueryFlags.Trash);
        var query = context.Cards
            .AsNoTracking()
            .Where(x => inTrash ? x.DeletedAt != null : x.DeletedAt == null);

        if (spec.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (spec.Difficulty is { } difficulty)
        {
            query = query.Where(x => x.Difficulty == difficulty);
        }

        var cards = await query.ToListAsync(ct).ConfigureAwait(false);
        if (cards.Count == 0)
        {
            return [];
        }

        var subjects = await context.Subjects
            .AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.Code })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var decks = await context.CardDecks
            .AsNoTracking()
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var links = await context.CardTagLinks.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var tags = await context.CardTags
            .AsNoTracking()
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var subjectNames = subjects.ToDictionary(x => x.Id, x => x.Name + " " + x.Code);
        var deckNames = decks.ToDictionary(x => x.Id, x => x.Name);
        var tagNames = tags.ToDictionary(x => x.Id, x => x.Name);
        var cardTags = links
            .GroupBy(x => x.CardId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => tagNames.GetValueOrDefault(x.TagId, string.Empty))
                      .Where(name => name.Length > 0)
                      .ToArray());

        if (spec.SubjectToken is { } subjectToken)
        {
            var subjectId = subjects
                .FirstOrDefault(x => Same(x.Code, subjectToken) || Same(x.Name, subjectToken))
                ?.Id
                ?? subjects.FirstOrDefault(x => Contains(x.Name, subjectToken))?.Id;
            if (subjectId is null)
            {
                return [];
            }

            cards = cards.Where(x => x.SubjectId == subjectId).ToList();
        }

        if (spec.DeckToken is { } deckToken)
        {
            var deckId = decks.FirstOrDefault(x => Same(x.Name, deckToken))?.Id
                ?? decks.FirstOrDefault(x => Contains(x.Name, deckToken))?.Id;
            if (deckId is null)
            {
                return [];
            }

            cards = cards.Where(x => x.DeckId == deckId).ToList();
        }

        var now = DateTimeOffset.Now;

        // Карточка едет рядом с выдачей: сортировкам, кроме релевантности, нужны её поля.
        var results = new List<(Card Card, CardSearchHit Hit)>();

        foreach (var card in cards)
        {
            var ownTags = cardTags.GetValueOrDefault(card.Id, []);
            if (!PassesFlags(card, spec.Flags, ownTags.Length > 0, now))
            {
                continue;
            }

            if (spec.Tags.Any(tag => !ownTags.Contains(tag, StringComparer.Ordinal))
                || spec.ExcludedTags.Any(tag => ownTags.Contains(tag, StringComparer.Ordinal)))
            {
                continue;
            }

            var title = Normalize(card.Front);
            var body = new StringBuilder()
                .Append(Normalize(card.Back)).Append(' ')
                .Append(Normalize(string.Join(' ', ownTags))).Append(' ')
                .Append(Normalize(card.Hint ?? string.Empty)).Append(' ')
                .Append(Normalize(card.Source ?? string.Empty)).Append(' ')
                .Append(Normalize(card.DeckId is { } deck ? deckNames.GetValueOrDefault(deck, string.Empty) : string.Empty)).Append(' ')
                .Append(Normalize(card.SubjectId is { } subject ? subjectNames.GetValueOrDefault(subject, string.Empty) : string.Empty))
                .ToString();

            if (!Matches(spec, title, body, out var matchedTitle))
            {
                continue;
            }

            // Ранжирование деградированное и намеренно простое: совпадение в лицевой стороне выше
            // совпадения в теле, закреплённые ещё выше, отложенные — ниже.
            var score = matchedTitle ? -2.0 : -1.0;
            if (card.IsPinned)
            {
                score -= 0.6;
            }

            if (options.ActiveSubjectId is { } active && card.SubjectId == active)
            {
                score -= 0.5;
            }

            if (card.IsSuspended)
            {
                score += 0.5;
            }

            results.Add((card, new CardSearchHit(card.Id, score, Excerpt(card.Back))));
        }

        // OrderBy в LINQ устойчив, поэтому при равных ключах сохраняется порядок выборки из БД.
        return Order(results, options.Sort)
            .Skip(Math.Max(0, options.Offset))
            .Take(Math.Max(1, options.Limit))
            .Select(row => row.Hit)
            .ToList();
    }

    /// <summary>
    /// Те же порядки, что у быстрого пути (<c>CardSearchRepository.OrderClause</c>), только на
    /// LINQ. Обе реализации обязаны выдавать одинаковый порядок — это проверяется тестами, которые
    /// гоняют каждый факт дважды.
    /// </summary>
    private static IEnumerable<(Card Card, CardSearchHit Hit)> Order(
        List<(Card Card, CardSearchHit Hit)> rows,
        CardSortOrder sort) => sort switch
        {
            CardSortOrder.RecentlyUpdated => rows
                .OrderByDescending(row => row.Card.IsPinned)
                .ThenByDescending(row => row.Card.UpdatedAt),
            CardSortOrder.RecentlyCreated => rows
                .OrderByDescending(row => row.Card.IsPinned)
                .ThenByDescending(row => row.Card.CreatedAt),
            CardSortOrder.Alphabetical => rows
                .OrderByDescending(row => row.Card.IsPinned)
                .ThenBy(row => row.Card.Front, StringComparer.CurrentCultureIgnoreCase),

            // Новые карточки срока не имеют — им место в конце очереди, а не в её начале.
            CardSortOrder.DueDate => rows
                .OrderByDescending(row => row.Card.IsPinned)
                .ThenBy(row => row.Card.DueAt is null)
                .ThenBy(row => row.Card.DueAt ?? DateTimeOffset.MaxValue),
            _ => rows.OrderBy(row => row.Hit.Score),
        };

    /// <summary>Полнотекстового индекса в этом режиме нет по определению.</summary>
    public Task<bool> IsFullTextAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);

    /// <summary>Перестраивать нечего — индекса нет.</summary>
    public Task<int> RebuildAsync(CancellationToken ct = default) => Task.FromResult(0);

    private static bool PassesFlags(Card card, CardQueryFlags flags, bool hasTags, DateTimeOffset now)
    {
        if (flags.HasFlag(CardQueryFlags.Hard) && card.Lapses == 0)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.New) && card.Repetitions != 0)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.DueToday) && (card.DueAt is null || card.DueAt > now))
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.Pinned) && !card.IsPinned)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.Suspended) && !card.IsSuspended)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.HasHint) && string.IsNullOrEmpty(card.Hint))
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.NoHint) && !string.IsNullOrEmpty(card.Hint))
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.HasSource) && string.IsNullOrEmpty(card.Source))
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.NoSource) && !string.IsNullOrEmpty(card.Source))
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.HasSubject) && card.SubjectId is null)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.NoSubject) && card.SubjectId is not null)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.HasDeck) && card.DeckId is null)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.NoDeck) && card.DeckId is not null)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.HasTags) && !hasTags)
        {
            return false;
        }

        if (flags.HasFlag(CardQueryFlags.NoTags) && hasTags)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Слово ищется префиксно (как в индексе — со второй буквы), фраза целиком, исключение отсеивает.
    /// </summary>
    private static bool Matches(CardQuerySpec spec, string title, string body, out bool matchedTitle)
    {
        matchedTitle = false;
        var haystack = title + " " + body;

        foreach (var term in spec.Terms)
        {
            var needle = Normalize(term.Text);
            switch (term.Kind)
            {
                case CardQueryTermKind.Exclude when ContainsWord(haystack, needle, prefix: false):
                    return false;
                case CardQueryTermKind.Exclude:
                    continue;
                case CardQueryTermKind.Phrase when !haystack.Contains(needle, StringComparison.Ordinal):
                    return false;
                case CardQueryTermKind.Prefix when !ContainsWord(haystack, needle, prefix: true):
                    return false;
            }

            if (term.Kind != CardQueryTermKind.Exclude
                && (title.Contains(needle, StringComparison.Ordinal)
                    || ContainsWord(title, needle, prefix: true)))
            {
                matchedTitle = true;
            }
        }

        return true;
    }

    private static bool ContainsWord(string haystack, string needle, bool prefix)
    {
        if (needle.Length == 0)
        {
            return false;
        }

        var start = 0;
        while (start < haystack.Length)
        {
            while (start < haystack.Length && !char.IsLetterOrDigit(haystack[start]))
            {
                start++;
            }

            var end = start;
            while (end < haystack.Length && char.IsLetterOrDigit(haystack[end]))
            {
                end++;
            }

            if (end > start)
            {
                var word = haystack.AsSpan(start, end - start);
                if (prefix ? word.StartsWith(needle, StringComparison.Ordinal)
                           : word.SequenceEqual(needle))
                {
                    return true;
                }
            }

            start = end + 1;
        }

        return false;
    }

    private static string Excerpt(string back)
    {
        var trimmed = back.Trim();
        return trimmed.Length <= 160 ? trimmed : trimmed[..160] + "…";
    }

    /// <summary>Регистр и «ё» складываются ровно так же, как это делает токенайзер индекса.</summary>
    private static string Normalize(string value) =>
        value.ToLower(CultureInfo.CurrentCulture).Replace('ё', 'е');

    private static bool Same(string value, string token) =>
        string.Equals(value, token, StringComparison.CurrentCultureIgnoreCase);

    private static bool Contains(string value, string token) =>
        CultureInfo.CurrentCulture.CompareInfo.IndexOf(value, token, CompareOptions.IgnoreCase) >= 0;
}
