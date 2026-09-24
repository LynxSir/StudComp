using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>
/// Поиск через виртуальную таблицу FTS5 (new_addons.md §4.2, §4.5) — быстрый путь.
/// </summary>
/// <remarks>
/// <para>
/// Единственное место проекта, где пишется сырой SQL поверх ADO.NET; прецедент прямой работы с
/// <c>Microsoft.Data.Sqlite</c> внутри <c>StudComp.Data</c> — <c>SqliteDatabaseBackupPort</c>.
/// Пользовательский текст <b>никогда</b> не склеивается со строкой запроса: и выражение
/// <c>MATCH</c>, и значения фильтров идут параметрами.
/// </para>
/// <para>
/// Копия SQL-фрагментов ниже намеренно живёт и здесь, и в миграции <c>AddCardSearchIndex</c>:
/// миграция — застывшая история и не имеет права зависеть от кода, который меняется.
/// </para>
/// </remarks>
internal sealed class CardSearchRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), ICardSearchRepository
{
    /// <summary>
    /// Складывает «ё» с «е» — ровно как одноимённый хелпер в миграции <c>AddCardSearchIndex</c>.
    /// Токенайзер складывает регистр кириллицы, но «ё» оставляет отдельной буквой, поэтому
    /// складываем её сами: и при записи в индекс, и в выражении запроса.
    /// </summary>
    private static string Fold(string expression) =>
        $"replace(replace({expression}, 'ё', 'е'), 'Ё', 'Е')";

    /// <summary>Метки карточки, склеенные через пробел. Псевдоним карточки — <c>c</c>.</summary>
    private const string TagsExpression = """
        COALESCE((SELECT group_concat(t.Name, ' ')
                    FROM CardTagLinks l
                    JOIN CardTags t ON t.Id = l.TagId
                   WHERE l.CardId = c.Id), '')
        """;

    /// <summary>Хвост с низким весом: подсказка, источник, названия колоды и предмета.</summary>
    private const string ExtraExpression = """
        COALESCE(c.Hint, '') || ' ' || COALESCE(c.Source, '') || ' '
          || COALESCE((SELECT d.Name FROM CardDecks d WHERE d.Id = c.DeckId), '') || ' '
          || COALESCE((SELECT s.Name || ' ' || s.Code FROM Subjects s WHERE s.Id = c.SubjectId), '')
        """;

    /// <summary>
    /// Веса колонок для <c>bm25</c>: заголовок вчетверо весомее тела, метки почти как заголовок.
    /// Порядок — как в объявлении таблицы: Front, Back, Tags, Extra. Имя таблицы пишется
    /// целиком: <c>MATCH</c> и функции FTS5 не принимают псевдоним из <c>FROM</c>.
    /// </summary>
    private const string Bm25Expression = "bm25(CardSearch, 12.0, 1.0, 8.0, 3.0)";

    /// <summary>
    /// Выражение <c>ORDER BY</c> под выбранный порядок. Строится только из констант — ни одного
    /// значения от пользователя, поэтому склейка со строкой запроса здесь безопасна.
    /// </summary>
    /// <param name="sort">Что просили в шапке раздела.</param>
    /// <param name="byScore">Есть ли в запросе вычисленный балл релевантности.</param>
    private static string OrderClause(CardSortOrder sort, bool byScore) => sort switch
    {
        CardSortOrder.RecentlyUpdated => "c.IsPinned DESC, c.UpdatedAt DESC",
        CardSortOrder.RecentlyCreated => "c.IsPinned DESC, c.CreatedAt DESC",
        CardSortOrder.Alphabetical => "c.IsPinned DESC, c.Front COLLATE NOCASE ASC",

        // Новые карточки срока не имеют — им место в конце очереди, а не в её начале.
        CardSortOrder.DueDate => "c.IsPinned DESC, c.DueAt IS NULL ASC, c.DueAt ASC",
        _ => byScore ? "Score" : "c.IsPinned DESC, c.UpdatedAt DESC",
    };

    /// <summary>«Свежесть» — правилась или повторялась за последнюю неделю.</summary>
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<CardSearchHit>> SearchAsync(
        CardQuerySpec spec,
        CardSearchOptions options,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var filter = await CardSearchFilter
            .BuildAsync(context, spec, ct)
            .ConfigureAwait(false);
        if (filter.IsImpossible)
        {
            return [];
        }

        var match = CardSearchMatchBuilder.Build(spec);
        var parameters = new List<SqliteParameter>(filter.Parameters)
        {
            new("@limit", Math.Max(1, options.Limit)),
            new("@offset", Math.Max(0, options.Offset)),
        };

        string sql;
        if (match is null)
        {
            // Полнотекстовой части нет — в индекс идти незачем: это листинг по фильтрам.
            sql = $"""
                SELECT c.Id, 0.0 AS Score, '' AS Snippet, c.Front AS Front
                  FROM Cards c
                 WHERE {filter.WhereClause}
                 ORDER BY {OrderClause(options.Sort, byScore: false)}
                 LIMIT @limit OFFSET @offset
                """;
        }
        else
        {
            parameters.Add(new SqliteParameter("@match", match));
            parameters.Add(new SqliteParameter("@highlightStart", CardSearchHit.HighlightStart));
            parameters.Add(new SqliteParameter("@highlightEnd", CardSearchHit.HighlightEnd));
            parameters.Add(new SqliteParameter("@fresh", DateTime.UtcNow - FreshnessWindow));

            var activeSubjectBoost = options.ActiveSubjectId is { } activeSubject
                ? "CASE WHEN c.SubjectId = @activeSubject THEN 0.5 ELSE 0.0 END"
                : "0.0";
            if (options.ActiveSubjectId is { } active)
            {
                parameters.Add(new SqliteParameter("@activeSubject", active));
            }

            // bm25 в SQLite отрицательный и «меньше — релевантнее», поэтому бустеры вычитаются, а
            // штраф отложенным карточкам прибавляется (new_addons.md §4.5).
            sql = $"""
                SELECT c.Id,
                       {Bm25Expression}
                         - CASE WHEN c.IsPinned = 1 THEN 0.6 ELSE 0.0 END
                         - {activeSubjectBoost}
                         - CASE WHEN c.UpdatedAt >= @fresh
                                  OR (c.LastReviewedAt IS NOT NULL AND c.LastReviewedAt >= @fresh)
                                THEN 0.3 ELSE 0.0 END
                         + CASE WHEN c.IsSuspended = 1 THEN 0.5 ELSE 0.0 END AS Score,
                       snippet(CardSearch, 1, @highlightStart, @highlightEnd, '…', 12) AS Snippet,
                       c.Front AS Front
                  FROM CardSearch
                  JOIN Cards c ON c.Id = CardSearch.CardId
                 WHERE CardSearch MATCH @match AND {filter.WhereClause}
                 ORDER BY {OrderClause(options.Sort, byScore: true)}
                 LIMIT @limit OFFSET @offset
                """;
        }

        var hits = await ReadHitsAsync(context, sql, parameters, ct).ConfigureAwait(false);

        // Буст переставляет строки по баллу, поэтому применим он только к релевантности: при явно
        // выбранном порядке пользователь просил алфавит или дату, а не «как посчитал индекс».
        return options.Sort == CardSortOrder.Relevance
            ? ApplyExactTitleBoost(hits, spec)
            : hits.ConvertAll(row => row.ToHit());
    }

    public async Task<bool> IsFullTextAvailableAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await IsAvailableAsync(context, ct).ConfigureAwait(false);
    }

    public async Task<int> RebuildAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        await context.Database
            .ExecuteSqlRawAsync("DELETE FROM CardSearch;", ct)
            .ConfigureAwait(false);

        // Строка собирается только из констант этого класса, пользовательского текста в ней нет;
        // локальная переменная нужна, чтобы анализатор EF1002 не принимал её за подставленный ввод.
        var insert = $"""
            INSERT INTO CardSearch(rowid, Front, Back, Tags, Extra, CardId)
            SELECT c.rowid, {Fold("c.Front")}, {Fold("c.Back")},
                   {Fold(TagsExpression)}, {Fold(ExtraExpression)}, c.Id
              FROM Cards c;
            """;

        return await context.Database
            .ExecuteSqlRawAsync(insert, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Есть ли виртуальная таблица в схеме — дешёвая проверка по <c>sqlite_master</c>.</summary>
    internal static async Task<bool> IsAvailableAsync(StudCompDbContext context, CancellationToken ct)
    {
        var found = await context.Database
            .SqlQuery<string>(
                $"SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name = 'CardSearch'")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return found.Count > 0;
    }

    private static async Task<List<ScoredRow>> ReadHitsAsync(
        StudCompDbContext context,
        string sql,
        List<SqliteParameter> parameters,
        CancellationToken ct)
    {
        await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var rows = new List<ScoredRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ScoredRow(
                Guid.Parse(reader.GetString(0)),
                reader.GetDouble(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
        }

        return rows;
    }

    /// <summary>
    /// Буст «запрос совпал с лицевой стороной целиком» считается уже по выданной странице, а не в
    /// <c>ORDER BY</c>: <c>lower()</c> в SQLite складывает регистр только у латиницы, и честного
    /// сравнения кириллицы без учёта регистра в SQL не написать. Порядок внутри страницы это меняет
    /// точно, между страницами — нет, но туда такая карточка и не попадёт: вес колонки <c>Front</c>
    /// поднимает её наверх ещё в индексе.
    /// </summary>
    private static IReadOnlyList<CardSearchHit> ApplyExactTitleBoost(
        List<ScoredRow> rows,
        CardQuerySpec spec)
    {
        var exact = spec.Raw.Trim();
        if (rows.Count < 2 || exact.Length == 0)
        {
            return rows.ConvertAll(row => row.ToHit());
        }

        return rows
            .Select(row => row.WithScore(row.Score - (Matches(row.Front, exact) ? 0.4 : 0.0)))
            .OrderBy(row => row.Score)
            .Select(row => row.ToHit())
            .ToList();

        static bool Matches(string front, string query) =>
            string.Equals(front.Trim(), query, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Строка выдачи вместе с лицевой стороной — она нужна только для буста.</summary>
    private readonly record struct ScoredRow(Guid CardId, double Score, string Snippet, string Front)
    {
        public ScoredRow WithScore(double score) => this with { Score = score };

        public CardSearchHit ToHit() => new(CardId, Score, Snippet);
    }

    /// <summary>Собранные фильтры запроса: кусок <c>WHERE</c> и его параметры.</summary>
    private sealed class CardSearchFilter
    {
        private CardSearchFilter(string whereClause, List<SqliteParameter> parameters, bool impossible)
        {
            WhereClause = whereClause;
            Parameters = parameters;
            IsImpossible = impossible;
        }

        public string WhereClause { get; }

        public List<SqliteParameter> Parameters { get; }

        /// <summary>Запрос заведомо пуст: назван предмет или колода, которых нет.</summary>
        public bool IsImpossible { get; }

        public static async Task<CardSearchFilter> BuildAsync(
            StudCompDbContext context,
            CardQuerySpec spec,
            CancellationToken ct)
        {
            var conditions = new List<string>();
            var parameters = new List<SqliteParameter>();

            // «Корзина» — единственный режим, в котором мягко удалённые становятся видимыми.
            conditions.Add(spec.Flags.HasFlag(CardQueryFlags.Trash)
                ? "c.DeletedAt IS NOT NULL"
                : "c.DeletedAt IS NULL");

            if (spec.SubjectToken is { } subjectToken)
            {
                var subjectId = await ResolveSubjectAsync(context, subjectToken, ct).ConfigureAwait(false);
                if (subjectId is null)
                {
                    return new CardSearchFilter("1 = 0", parameters, impossible: true);
                }

                conditions.Add("c.SubjectId = @subjectId");
                parameters.Add(new SqliteParameter("@subjectId", subjectId.Value));
            }

            if (spec.DeckToken is { } deckToken)
            {
                var deckId = await ResolveDeckAsync(context, deckToken, ct).ConfigureAwait(false);
                if (deckId is null)
                {
                    return new CardSearchFilter("1 = 0", parameters, impossible: true);
                }

                conditions.Add("c.DeckId = @deckId");
                parameters.Add(new SqliteParameter("@deckId", deckId.Value));
            }

            if (spec.Kind is { } kind)
            {
                conditions.Add("c.Kind = @kind");
                parameters.Add(new SqliteParameter("@kind", (int)kind));
            }

            if (spec.Difficulty is { } difficulty)
            {
                conditions.Add("c.Difficulty = @difficulty");
                parameters.Add(new SqliteParameter("@difficulty", (int)difficulty));
            }

            AddFlagConditions(spec.Flags, conditions, parameters);
            AddTagConditions(spec, conditions, parameters);

            return new CardSearchFilter(string.Join(" AND ", conditions), parameters, impossible: false);
        }

        private static void AddFlagConditions(
            CardQueryFlags flags,
            List<string> conditions,
            List<SqliteParameter> parameters)
        {
            if (flags.HasFlag(CardQueryFlags.Hard))
            {
                conditions.Add("c.Lapses > 0");
            }

            if (flags.HasFlag(CardQueryFlags.New))
            {
                conditions.Add("c.Repetitions = 0");
            }

            if (flags.HasFlag(CardQueryFlags.DueToday))
            {
                conditions.Add("c.DueAt IS NOT NULL AND c.DueAt <= @now");
                parameters.Add(new SqliteParameter("@now", DateTime.UtcNow));
            }

            if (flags.HasFlag(CardQueryFlags.Pinned))
            {
                conditions.Add("c.IsPinned = 1");
            }

            if (flags.HasFlag(CardQueryFlags.Suspended))
            {
                conditions.Add("c.IsSuspended = 1");
            }

            AddPresence(flags, CardQueryFlags.HasHint, CardQueryFlags.NoHint, "c.Hint", conditions);
            AddPresence(flags, CardQueryFlags.HasSource, CardQueryFlags.NoSource, "c.Source", conditions);

            if (flags.HasFlag(CardQueryFlags.HasSubject))
            {
                conditions.Add("c.SubjectId IS NOT NULL");
            }

            if (flags.HasFlag(CardQueryFlags.NoSubject))
            {
                conditions.Add("c.SubjectId IS NULL");
            }

            if (flags.HasFlag(CardQueryFlags.HasDeck))
            {
                conditions.Add("c.DeckId IS NOT NULL");
            }

            if (flags.HasFlag(CardQueryFlags.NoDeck))
            {
                conditions.Add("c.DeckId IS NULL");
            }

            if (flags.HasFlag(CardQueryFlags.HasTags))
            {
                conditions.Add("EXISTS (SELECT 1 FROM CardTagLinks l WHERE l.CardId = c.Id)");
            }

            if (flags.HasFlag(CardQueryFlags.NoTags))
            {
                conditions.Add("NOT EXISTS (SELECT 1 FROM CardTagLinks l WHERE l.CardId = c.Id)");
            }
        }

        private static void AddPresence(
            CardQueryFlags flags,
            CardQueryFlags present,
            CardQueryFlags absent,
            string column,
            List<string> conditions)
        {
            if (flags.HasFlag(present))
            {
                conditions.Add($"{column} IS NOT NULL AND {column} <> ''");
            }

            if (flags.HasFlag(absent))
            {
                conditions.Add($"({column} IS NULL OR {column} = '')");
            }
        }

        private static void AddTagConditions(
            CardQuerySpec spec,
            List<string> conditions,
            List<SqliteParameter> parameters)
        {
            for (var i = 0; i < spec.Tags.Count; i++)
            {
                conditions.Add($"""
                    EXISTS (SELECT 1 FROM CardTagLinks l JOIN CardTags t ON t.Id = l.TagId
                             WHERE l.CardId = c.Id AND t.Name = @tag{i})
                    """);
                parameters.Add(new SqliteParameter($"@tag{i}", spec.Tags[i]));
            }

            for (var i = 0; i < spec.ExcludedTags.Count; i++)
            {
                conditions.Add($"""
                    NOT EXISTS (SELECT 1 FROM CardTagLinks l JOIN CardTags t ON t.Id = l.TagId
                                 WHERE l.CardId = c.Id AND t.Name = @notTag{i})
                    """);
                parameters.Add(new SqliteParameter($"@notTag{i}", spec.ExcludedTags[i]));
            }
        }

        /// <summary>
        /// Предмет по имени или коду. Сравнение идёт в памяти: предметов десятки, а <c>lower()</c> в
        /// SQLite складывает регистр только у латиницы — «Матан» и «матан» в SQL не совпали бы.
        /// </summary>
        private static async Task<Guid?> ResolveSubjectAsync(
            StudCompDbContext context,
            string token,
            CancellationToken ct)
        {
            var subjects = await context.Subjects
                .AsNoTracking()
                .Select(x => new { x.Id, x.Name, x.Code })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var match =
                subjects.FirstOrDefault(x => Same(x.Code, token))
                ?? subjects.FirstOrDefault(x => Same(x.Name, token))
                ?? subjects.FirstOrDefault(x => Contains(x.Name, token));

            return match?.Id;
        }

        private static async Task<Guid?> ResolveDeckAsync(
            StudCompDbContext context,
            string token,
            CancellationToken ct)
        {
            var decks = await context.CardDecks
                .AsNoTracking()
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var match =
                decks.FirstOrDefault(x => Same(x.Name, token))
                ?? decks.FirstOrDefault(x => Contains(x.Name, token));

            return match?.Id;
        }

        private static bool Same(string value, string token) =>
            string.Equals(value, token, StringComparison.CurrentCultureIgnoreCase);

        private static bool Contains(string value, string token) =>
            CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                value, token, CompareOptions.IgnoreCase) >= 0;
    }
}

/// <summary>
/// Перевод разобранного запроса в выражение <c>MATCH</c> синтаксиса FTS5 (new_addons.md §4.4).
/// </summary>
internal static class CardSearchMatchBuilder
{
    /// <summary>
    /// Собрать выражение или вернуть <see langword="null"/>, если искать в индексе нечего.
    /// </summary>
    /// <remarks>
    /// Каждое слово оборачивается в двойные кавычки с удвоением внутренних — без этого один
    /// случайный апостроф, звёздочка или скобка в запросе роняли бы выполнение. Запрос из одних
    /// исключений (<c>-лейбниц</c>) выражением FTS5 быть не может: <c>NOT</c> требует левой части,
    /// поэтому такой запрос уходит в обычный листинг по фильтрам.
    /// </remarks>
    public static string? Build(CardQuerySpec spec)
    {
        var positives = new List<string>();
        var negatives = new List<string>();

        foreach (var term in spec.Terms)
        {
            var quoted = Quote(term.Text);
            switch (term.Kind)
            {
                case CardQueryTermKind.Prefix:
                    positives.Add(quoted + "*");
                    break;
                case CardQueryTermKind.Phrase:
                    positives.Add(quoted);
                    break;
                case CardQueryTermKind.Exclude:
                    negatives.Add(quoted);
                    break;
            }
        }

        if (positives.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder(string.Join(" AND ", positives));
        foreach (var negative in negatives)
        {
            builder.Append(" NOT ").Append(negative);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Кавычки удваиваются, «ё» складывается с «е» — обе стороны сравнения обязаны быть сложены
    /// одинаково, иначе «емкость» не найдёт «ёмкость» (проверено тестом).
    /// </summary>
    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\"\"").Replace('ё', 'е').Replace('Ё', 'Е') + "\"";
}
