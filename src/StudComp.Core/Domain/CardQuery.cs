using System.Globalization;
using System.Text;

namespace StudComp.Core.Domain;

/// <summary>Чем слово является в полнотекстовой части запроса (new_addons.md §4.4).</summary>
public enum CardQueryTermKind
{
    /// <summary>Обычное слово — ищется префиксно (<c>интеграл</c> находит «интегральный»).</summary>
    Prefix = 0,

    /// <summary>Точная фраза в кавычках.</summary>
    Phrase = 1,

    /// <summary>Слово со знаком минус — исключающее.</summary>
    Exclude = 2,
}

/// <summary>Одно слово или фраза из полнотекстовой части запроса.</summary>
/// <param name="Text">Чистый текст без кавычек и лишних пробелов.</param>
/// <param name="Kind">Как его искать.</param>
public sealed record CardQueryTerm(string Text, CardQueryTermKind Kind);

/// <summary>
/// Служебные признаки запроса — пресеты состояния и фильтры «есть/нет» (new_addons.md §4.4).
/// Флаги, а не полтора десятка отдельных полей: все они складываются логическим И.
/// </summary>
[Flags]
public enum CardQueryFlags
{
    None = 0,

    /// <summary>«трудные» — карточки, которые уже забывали.</summary>
    Hard = 1 << 0,

    /// <summary>«новые» — ни разу не показанные.</summary>
    New = 1 << 1,

    /// <summary>«сегодня» — подошёл срок повторения.</summary>
    DueToday = 1 << 2,

    /// <summary>«закреплённые».</summary>
    Pinned = 1 << 3,

    /// <summary>«отложенные» — исключённые из тренировок.</summary>
    Suspended = 1 << 4,

    /// <summary>«корзина» — искать среди мягко удалённых вместо живых.</summary>
    Trash = 1 << 5,

    HasHint = 1 << 6,
    NoHint = 1 << 7,
    HasTags = 1 << 8,
    NoTags = 1 << 9,
    HasSubject = 1 << 10,
    NoSubject = 1 << 11,
    HasDeck = 1 << 12,
    NoDeck = 1 << 13,
    HasSource = 1 << 14,
    NoSource = 1 << 15,
}

/// <summary>
/// Разобранная строка поиска (new_addons.md §4.4). Ничего не знает ни о SQLite, ни о выражении
/// <c>MATCH</c> — перевод в него живёт в <c>StudComp.Data</c>.
/// </summary>
/// <param name="Raw">Исходная строка — её показывает пустое состояние «Создать карточку „…“?».</param>
/// <param name="Terms">Полнотекстовая часть: слова, фразы и исключения.</param>
/// <param name="Tags">Метки из <c>#метка</c>, нормализованные.</param>
/// <param name="ExcludedTags">Метки из <c>-#метка</c>.</param>
/// <param name="SubjectToken">Значение <c>@предмет</c> — разрешается по имени или коду в слое данных.</param>
/// <param name="DeckToken">Значение <c>колода:…</c> — разрешается по имени в слое данных.</param>
/// <param name="Kind">Фильтр <c>тип:…</c>.</param>
/// <param name="Difficulty">Фильтр <c>сложность:…</c>.</param>
/// <param name="Flags">Пресеты состояния и фильтры «есть/нет».</param>
public sealed record CardQuerySpec(
    string Raw,
    IReadOnlyList<CardQueryTerm> Terms,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ExcludedTags,
    string? SubjectToken,
    string? DeckToken,
    CardKind? Kind,
    CardDifficulty? Difficulty,
    CardQueryFlags Flags)
{
    /// <summary>Есть ли полнотекстовая часть — только ради неё имеет смысл идти в поисковый индекс.</summary>
    public bool HasFullTextPart => Terms.Count > 0;

    /// <summary>Запрос ничего не спрашивает: показываем недавние и закреплённые.</summary>
    public bool IsEmpty =>
        Terms.Count == 0
        && Tags.Count == 0
        && ExcludedTags.Count == 0
        && SubjectToken is null
        && DeckToken is null
        && Kind is null
        && Difficulty is null
        && Flags == CardQueryFlags.None;
}

/// <summary>
/// Разбор строки поиска картотеки (new_addons.md §4.4) — чистая функция, только BCL, таблично
/// тестируется. Соседи по папке — <see cref="WeekParityCalculator"/>, <see cref="LinearRegression"/>,
/// <see cref="AcademicHoursCalculator"/>.
/// </summary>
/// <remarks>
/// Главный инвариант: <b>пользователь никогда не получает ошибку синтаксиса</b>. Всё, что не
/// разобралось как оператор, уходит в полнотекстовую часть; незакрытая кавычка, одинокий минус и
/// мусорный ввод дают менее точный результат, но не исключение.
/// </remarks>
public static class CardQuery
{
    /// <summary>Разобрать строку поиска. <see langword="null"/> и пробелы дают пустой запрос.</summary>
    public static CardQuerySpec Parse(string? query)
    {
        var raw = query ?? string.Empty;

        var terms = new List<CardQueryTerm>();
        var tags = new List<string>();
        var excludedTags = new List<string>();
        string? subject = null;
        string? deck = null;
        CardKind? kind = null;
        CardDifficulty? difficulty = null;
        var flags = CardQueryFlags.None;

        foreach (var token in SplitTokens(raw))
        {
            var body = token;
            var negated = false;

            // Одинокий минус оператором не считается — его отсеет проверка на буквы в AddTerm.
            if (body.Length > 1 && body[0] == '-')
            {
                negated = true;
                body = body[1..];
            }

            if (TryReadTag(body, out var tag))
            {
                (negated ? excludedTags : tags).Add(tag);
                continue;
            }

            if (body.Length > 1 && body[0] == '@' && Clean(body[1..]) is { Length: > 0 } subjectToken)
            {
                subject = subjectToken;
                continue;
            }

            if (TryReadOperator(body, out var name, out var rawValue))
            {
                var value = Normalize(rawValue);
                switch (name)
                {
                    case "колода" when value.Length > 0:
                        deck = Clean(rawValue);
                        continue;
                    case "предмет" when value.Length > 0:
                        subject = Clean(rawValue);
                        continue;
                    case "метка" when value.Length > 0:
                        (negated ? excludedTags : tags).Add(value);
                        continue;
                    case "тип" when TryReadKind(value) is { } parsedKind:
                        kind = parsedKind;
                        continue;
                    case "сложность" when TryReadDifficulty(value) is { } parsedDifficulty:
                        difficulty = parsedDifficulty;
                        continue;
                    case "есть" when TryReadPresence(value, present: true) is { } present:
                        flags |= present;
                        continue;
                    case "нет" when TryReadPresence(value, present: false) is { } absent:
                        flags |= absent;
                        continue;
                }

                // Незнакомый оператор или непонятное значение — не ошибка: слово идёт в поиск целиком.
            }

            if (!negated && TryReadPreset(Normalize(body)) is { } preset)
            {
                flags |= preset;
                continue;
            }

            AddTerm(terms, body, negated);
        }

        return new CardQuerySpec(
            raw,
            terms,
            Distinct(tags),
            Distinct(excludedTags),
            subject,
            deck,
            kind,
            difficulty,
            flags);
    }

    /// <summary>
    /// Разбить строку на токены, не разрывая кавычки. Незакрытая кавычка забирает остаток строки —
    /// это осознанно: пользователь ещё печатает, ронять поиск на полуслове нельзя.
    /// </summary>
    private static List<string> SplitTokens(string input)
    {
        var tokens = new List<string>();
        var buffer = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                buffer.Append(ch);
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                Flush(tokens, buffer);
                continue;
            }

            buffer.Append(ch);
        }

        Flush(tokens, buffer);
        return tokens;

        static void Flush(List<string> target, StringBuilder buffer)
        {
            if (buffer.Length > 0)
            {
                target.Add(buffer.ToString());
                buffer.Clear();
            }
        }
    }

    private static void AddTerm(List<CardQueryTerm> terms, string body, bool negated)
    {
        var isPhrase = body.Length > 0 && body[0] == '"';
        var text = isPhrase ? Unquote(body) : Clean(body);

        if (!HasLetterOrDigit(text))
        {
            return;
        }

        var kind = negated
            ? CardQueryTermKind.Exclude
            : isPhrase ? CardQueryTermKind.Phrase : CardQueryTermKind.Prefix;

        terms.Add(new CardQueryTerm(text, kind));
    }

    private static bool TryReadTag(string body, out string tag)
    {
        tag = string.Empty;
        if (body.Length <= 1 || body[0] != '#')
        {
            return false;
        }

        tag = Normalize(body[1..]);
        return tag.Length > 0;
    }

    /// <summary>
    /// Разобрать <c>имя:значение</c>. Кавычки в значении допустимы: <c>колода:"к экзамену"</c>.
    /// </summary>
    private static bool TryReadOperator(string body, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;

        var separator = body.IndexOf(':');
        if (separator <= 0 || separator == body.Length - 1)
        {
            return false;
        }

        name = Normalize(body[..separator]);
        value = body[(separator + 1)..];
        return name.Length > 0;
    }

    private static CardKind? TryReadKind(string value) => value switch
    {
        "термин" or "термины" => CardKind.Term,
        "вопрос" or "вопросы" => CardKind.Question,
        "формула" or "формулы" => CardKind.Formula,
        "код" => CardKind.Code,
        "факт" or "факты" => CardKind.Fact,
        _ => null,
    };

    private static CardDifficulty? TryReadDifficulty(string value) => value switch
    {
        "легкая" or "легкие" or "простая" => CardDifficulty.Easy,
        "обычная" or "обычные" or "нормальная" => CardDifficulty.Normal,
        "трудная" or "трудные" or "сложная" or "сложные" => CardDifficulty.Hard,
        _ => null,
    };

    private static CardQueryFlags? TryReadPresence(string value, bool present) => value switch
    {
        "подсказка" or "подсказки" => present ? CardQueryFlags.HasHint : CardQueryFlags.NoHint,
        "метка" or "метки" => present ? CardQueryFlags.HasTags : CardQueryFlags.NoTags,
        "предмет" or "предмета" => present ? CardQueryFlags.HasSubject : CardQueryFlags.NoSubject,
        "колода" or "колоды" => present ? CardQueryFlags.HasDeck : CardQueryFlags.NoDeck,
        "источник" or "источника" => present ? CardQueryFlags.HasSource : CardQueryFlags.NoSource,
        _ => null,
    };

    private static CardQueryFlags? TryReadPreset(string value) => value switch
    {
        "трудные" => CardQueryFlags.Hard,
        "новые" => CardQueryFlags.New,
        "сегодня" => CardQueryFlags.DueToday,
        "закрепленные" => CardQueryFlags.Pinned,
        "отложенные" => CardQueryFlags.Suspended,
        "корзина" => CardQueryFlags.Trash,
        _ => null,
    };

    /// <summary>Снять внешние кавычки и схлопнуть удвоенные внутренние.</summary>
    private static string Unquote(string value) => Clean(value.Trim('"').Replace("\"\"", "\""));

    /// <summary>Убрать кавычки и схлопнуть пробелы — то, что уйдёт в индекс как слово или фраза.</summary>
    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var ch in value)
        {
            if (ch == '"')
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (builder.Length > 0 && !lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Ключевые слова сравниваются в нижнем регистре и с «ё» как «е» — ровно так же, как складывает
    /// буквы токенайзер поискового индекса (new_addons.md §4.1).
    /// </summary>
    private static string Normalize(string value) =>
        Clean(value).ToLower(CultureInfo.InvariantCulture).Replace('ё', 'е');

    private static bool HasLetterOrDigit(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> Distinct(List<string> values)
    {
        if (values.Count <= 1)
        {
            return values;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
