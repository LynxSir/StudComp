using System.Text;

namespace StudComp.Core.Domain;

/// <summary>
/// Приведение «латеховских» ограничителей формул к тем, которые понимает движок разметки.
/// </summary>
/// <remarks>
/// Такую запись выдают языковые модели, и конспекты студентов ею полны: строчная формула в
/// круглых скобках с обратной косой, выключная — в квадратных. Markdig знает только форму с
/// долларами, поэтому до разбора одно переписывается в другое. Внутри кода и кодовых заборов
/// ничего не меняется. Метод не бросает исключений на любом вводе.
/// </remarks>
public static class MathDelimiters
{
    /// <summary>
    /// Защитить окружения TeX от разрыва Markdown на абзацы и списки. Переводы строк заменяются
    /// пробелами той же длины: позиции изображений после формулы остаются точными.
    /// </summary>
    public static string NormalizeMultilineEnvironments(string markdown)
    {
        if (!markdown.Contains("\\begin{", StringComparison.Ordinal)) return markdown;
        var code = MarkdownScanner.CodeRanges(markdown);
        char[]? result = null;
        for (var start = 0; start < markdown.Length; start++)
        {
            if (markdown[start] != '$' || IsEscaped(markdown, start)
                || MarkdownScanner.IsInsideCode(code, start)) continue;
            if (start > 0 && !char.IsWhiteSpace(markdown[start - 1])
                && !char.IsPunctuation(markdown[start - 1])) continue;
            var count = start + 1 < markdown.Length && markdown[start + 1] == '$' ? 2 : 1;
            var bodyStart = start + count;
            var close = bodyStart;
            while (close < markdown.Length && (markdown[close] != '$' || IsEscaped(markdown, close))) close++;
            if (close == markdown.Length) break;
            var closeCount = 1;
            while (close + closeCount < markdown.Length && markdown[close + closeCount] == '$') closeCount++;
            var end = close + closeCount;
            var body = markdown.AsSpan(bodyStart, close - bodyStart);
            if (closeCount == count && (end == markdown.Length || char.IsWhiteSpace(markdown[end])
                || char.IsPunctuation(markdown[end])) && body.Contains("\\begin{", StringComparison.Ordinal)
                && body.Contains("\\end{", StringComparison.Ordinal)
                && !code.Any(range => range.Start < close && range.End > bodyStart))
            {
                for (var at = bodyStart; at < close; at++)
                    if (markdown[at] is '\n' or '\r') (result ??= markdown.ToCharArray())[at] = ' ';
            }
            start = end - 1;
        }
        return result is null ? markdown : new string(result);
    }

    private static bool IsEscaped(string source, int position)
    {
        var count = 0;
        while (position > 0 && source[--position] == '\\') count++;
        return count % 2 != 0;
    }

    /// <summary>Обратная косая черта числом: в виде литерала она только мешает читать код.</summary>
    private const char Escape = (char)92;

    /// <summary>Переписать <c>\(…\)</c> в <c>$…$</c>, а <c>\[…\]</c> в <c>$$…$$</c>.</summary>
    public static string Normalize(string? markdown)
    {
        var source = markdown ?? string.Empty;
        if (source.Length < 4 || !source.Contains(Escape))
        {
            return source;
        }

        var code = MarkdownScanner.CodeRanges(source);
        var builder = new StringBuilder(source.Length);
        var position = 0;

        while (position < source.Length)
        {
            var open = FindOpening(source, position, code, out var display);
            if (open < 0)
            {
                builder.Append(source, position, source.Length - position);
                return builder.ToString();
            }

            var close = FindClosing(source, open + 2, code, display);
            if (close < 0)
            {
                // Незакрытая последовательность — это просто текст, трогать его нельзя.
                builder.Append(source, position, source.Length - position);
                return builder.ToString();
            }

            var marker = display ? "$$" : "$";
            builder.Append(source, position, open - position)
                .Append(marker)
                .Append(source, open + 2, close - open - 2)
                .Append(marker);

            position = close + 2;
        }

        return builder.ToString();
    }

    private static int FindOpening(string source, int from, List<(int Start, int End)> code, out bool display)
    {
        display = false;

        for (var i = from; i + 1 < source.Length; i++)
        {
            if (source[i] != Escape || (source[i + 1] != '(' && source[i + 1] != '['))
            {
                continue;
            }

            // Экранированная обратная косая: «\(» — это косая и обычная скобка, не ограничитель.
            if (i > 0 && source[i - 1] == Escape)
            {
                continue;
            }

            if (MarkdownScanner.IsInsideCode(code, i))
            {
                continue;
            }

            display = source[i + 1] == '[';
            return i;
        }

        return -1;
    }

    private static int FindClosing(string source, int from, List<(int Start, int End)> code, bool display)
    {
        var expected = display ? ']' : ')';

        for (var i = from; i + 1 < source.Length; i++)
        {
            if (source[i] != Escape || source[i + 1] != expected)
            {
                continue;
            }

            if (i > from && source[i - 1] == Escape)
            {
                continue;
            }

            if (MarkdownScanner.IsInsideCode(code, i))
            {
                continue;
            }

            return i;
        }

        return -1;
    }
}
