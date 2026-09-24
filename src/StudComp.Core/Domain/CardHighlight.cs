using System.Text;

namespace StudComp.Core.Domain;

/// <summary>Кусок текста выдачи: обычный или совпавший с запросом.</summary>
/// <param name="Text">Сам текст куска.</param>
/// <param name="IsMatch">Показывать ли его выделенным.</param>
public readonly record struct CardTextSegment(string Text, bool IsMatch);

/// <summary>
/// Разрезание текста карточки на подсвеченные и обычные куски (new_addons.md §4.5). Вьюмодель
/// превращает их в последовательность <c>Run</c>, никакого HTML в проекте не появляется.
/// </summary>
/// <remarks>
/// <para>
/// Два источника подсветки. Оборот приходит из индекса уже размеченным (<c>snippet()</c> ставит
/// вокруг совпадений управляющие маркеры) — его разбирает <see cref="FromSnippet"/>. Лицевую
/// сторону индекс не размечает, да и деградированный поиск (§4.3) снипетов не отдаёт вовсе,
/// поэтому там совпадения ищутся по разобранным термам запроса — <see cref="FromTerms"/>.
/// </para>
/// <para>
/// Инвариант тот же, что у <see cref="CardQuery"/>: <b>любая пользовательская строка разбирается
/// без исключения</b>. Непарный маркер, пустой терм и мусорный ввод дают менее точную подсветку,
/// но не роняют выдачу.
/// </para>
/// </remarks>
public static class CardHighlight
{
    /// <summary>
    /// Маркер начала совпадения. Управляющий символ, а не разметка: в тексте карточки его не бывает.
    /// Значение живёт здесь, потому что ставит его слой данных, а разбирает вьюмодель.
    /// </summary>
    public const string MarkerStart = "\u0001";

    /// <summary>Маркер конца совпадения.</summary>
    public const string MarkerEnd = "\u0002";

    private const char MarkerStartChar = '\u0001';
    private const char MarkerEndChar = '\u0002';

    /// <summary>Многоточие обрезки — тот же символ, что просит FTS5 в <c>snippet()</c>.</summary>
    private const string Ellipsis = "…";

    /// <summary>
    /// Разобрать отрывок из индекса. Непарные маркеры просто отбрасываются: портить текст
    /// пользователя косметика не вправе.
    /// </summary>
    public static IReadOnlyList<CardTextSegment> FromSnippet(string? snippet)
    {
        if (string.IsNullOrEmpty(snippet))
        {
            return [];
        }

        var segments = new List<CardTextSegment>();
        var buffer = new StringBuilder(snippet.Length);
        var inMatch = false;

        foreach (var ch in snippet)
        {
            switch (ch)
            {
                case MarkerStartChar when !inMatch:
                    Flush(segments, buffer, isMatch: false);
                    inMatch = true;
                    continue;

                case MarkerEndChar when inMatch:
                    Flush(segments, buffer, isMatch: true);
                    inMatch = false;
                    continue;

                case MarkerStartChar:
                case MarkerEndChar:
                    // Непарный маркер — молча пропускаем, текст вокруг остаётся как есть.
                    continue;

                default:
                    buffer.Append(ch);
                    continue;
            }
        }

        Flush(segments, buffer, inMatch);
        return segments;
    }

    /// <summary>
    /// Найти совпадения термов запроса в тексте. Обычные слова ищутся с начала слова (как
    /// префиксный поиск в индексе), фразы — целиком, исключающие термы не подсвечиваются.
    /// </summary>
    /// <param name="text">Исходный текст, например лицевая сторона карточки.</param>
    /// <param name="terms">Термы разобранного запроса.</param>
    /// <param name="maxLength">Обрезать текст до N символов с многоточием; <c>0</c> — не обрезать.</param>
    public static IReadOnlyList<CardTextSegment> FromTerms(
        string? text,
        IReadOnlyList<CardQueryTerm>? terms,
        int maxLength = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var trimmed = text;
        var truncated = false;
        if (maxLength > 0 && trimmed.Length > maxLength)
        {
            trimmed = trimmed[..maxLength];
            truncated = true;
        }

        var ranges = terms is null || terms.Count == 0
            ? []
            : FindRanges(trimmed, terms);

        if (ranges.Count == 0)
        {
            return [new CardTextSegment(truncated ? trimmed + Ellipsis : trimmed, false)];
        }

        var segments = new List<CardTextSegment>((ranges.Count * 2) + 1);
        var cursor = 0;

        foreach (var (start, length) in ranges)
        {
            if (start > cursor)
            {
                segments.Add(new CardTextSegment(trimmed[cursor..start], false));
            }

            segments.Add(new CardTextSegment(trimmed.Substring(start, length), true));
            cursor = start + length;
        }

        var tail = truncated ? trimmed[cursor..] + Ellipsis : trimmed[cursor..];
        if (tail.Length > 0)
        {
            segments.Add(new CardTextSegment(tail, false));
        }

        return segments;
    }

    /// <summary>Собрать куски обратно в строку — для тултипов и подписей, которым подсветка не нужна.</summary>
    public static string Flatten(IReadOnlyList<CardTextSegment>? segments)
    {
        if (segments is null || segments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            builder.Append(segment.Text);
        }

        return builder.ToString();
    }

    private static List<(int Start, int Length)> FindRanges(string text, IReadOnlyList<CardQueryTerm> terms)
    {
        var folded = Fold(text);
        var found = new List<(int Start, int Length)>();

        foreach (var term in terms)
        {
            if (term is null || term.Kind == CardQueryTermKind.Exclude)
            {
                continue;
            }

            var needle = Fold(term.Text);
            if (needle.Length == 0)
            {
                continue;
            }

            var from = 0;
            while (from <= folded.Length - needle.Length)
            {
                var at = folded.IndexOf(needle, from, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                // Обычное слово ищется префиксно — значит и подсвечивается от начала слова, иначе
                // «интеграл» подсветил бы середину слова «полуинтеграл» и сбил бы с толку.
                if (term.Kind != CardQueryTermKind.Prefix || IsWordStart(folded, at))
                {
                    found.Add((at, needle.Length));
                }

                from = at + 1;
            }
        }

        return Merge(found);
    }

    /// <summary>Склеить пересекающиеся и соприкасающиеся куски — иначе на стыке получится рваный текст.</summary>
    private static List<(int Start, int Length)> Merge(List<(int Start, int Length)> ranges)
    {
        if (ranges.Count <= 1)
        {
            return ranges;
        }

        ranges.Sort(static (a, b) => a.Start != b.Start
            ? a.Start.CompareTo(b.Start)
            : b.Length.CompareTo(a.Length));

        var merged = new List<(int Start, int Length)>(ranges.Count);
        var (start, length) = ranges[0];

        for (var i = 1; i < ranges.Count; i++)
        {
            var (nextStart, nextLength) = ranges[i];
            if (nextStart <= start + length)
            {
                length = Math.Max(length, nextStart + nextLength - start);
                continue;
            }

            merged.Add((start, length));
            (start, length) = (nextStart, nextLength);
        }

        merged.Add((start, length));
        return merged;
    }

    private static bool IsWordStart(string text, int index) =>
        index == 0 || !char.IsLetterOrDigit(text[index - 1]);

    /// <summary>
    /// Складывание регистра и «ё» с «е» — ровно как в <see cref="CardQuery"/> и в поисковом индексе.
    /// Посимвольное, чтобы позиции в сложенной строке совпадали с позициями в исходной.
    /// </summary>
    private static string Fold(string value)
    {
        var buffer = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var lower = char.ToLowerInvariant(value[i]);
            buffer[i] = lower == 'ё' ? 'е' : lower;
        }

        return new string(buffer);
    }

    private static void Flush(List<CardTextSegment> segments, StringBuilder buffer, bool isMatch)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        segments.Add(new CardTextSegment(buffer.ToString(), isMatch));
        buffer.Clear();
    }
}
