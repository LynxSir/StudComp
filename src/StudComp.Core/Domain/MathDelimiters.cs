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
