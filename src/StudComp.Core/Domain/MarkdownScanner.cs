namespace StudComp.Core.Domain;

/// <summary>
/// Разбор служебной структуры размеченного текста, общий для всех, кто ищет в нём что-то своё.
/// </summary>
/// <remarks>
/// Реализация кодовых заборов и вставок кода живёт тут в единственном экземпляре: пример
/// «<c>![a](a.png)</c>» внутри <c>`…`</c> или <c>```…```</c> не картинка, а «<c>\(x\)</c>» там же
/// не формула, и расходиться эти два ответа не должны.
/// </remarks>
internal static class MarkdownScanner
{
    /// <summary>
    /// Диапазоны текста, занятые вставками кода и кодовыми заборами. Внутри них разметку не трогают.
    /// </summary>
    public static List<(int Start, int End)> CodeRanges(string markdown)
    {
        var code = new List<(int Start, int End)>();
        for (var i = 0; i < markdown.Length;)
        {
            var lineStart = i == 0 || markdown[i - 1] == '\n';
            var token = i;
            if (lineStart) while (token < markdown.Length && token - i < 3 && markdown[token] == ' ') token++;
            var symbol = token < markdown.Length ? markdown[token] : '\0';
            if (symbol is not ('`' or '~')) { i++; continue; }
            var end = token;
            while (end < markdown.Length && markdown[end] == symbol) end++;
            var length = end - token;
            if (lineStart && length >= 3)
            {
                var close = markdown.IndexOf('\n', end);
                var blockEnd = markdown.Length;
                while (close >= 0 && close + 1 < markdown.Length)
                {
                    var start = close + 1;
                    var at = start;
                    while (at < markdown.Length && at - start < 3 && markdown[at] == ' ') at++;
                    var run = at;
                    while (at < markdown.Length && markdown[at] == symbol) at++;
                    var next = markdown.IndexOf('\n', at);
                    var tail = markdown[at..(next < 0 ? markdown.Length : next)];
                    if (at - run >= length && string.IsNullOrWhiteSpace(tail))
                    { blockEnd = next < 0 ? markdown.Length : next + 1; break; }
                    close = next;
                }
                code.Add((i, blockEnd));
                i = blockEnd;
            }
            else if (symbol == '`')
            {
                var delimiter = new string('`', length);
                var close = markdown.IndexOf(delimiter, end, StringComparison.Ordinal);
                while (close >= 0 && (close > 0 && markdown[close - 1] == '`'
                    || close + length < markdown.Length && markdown[close + length] == '`'))
                    close = markdown.IndexOf(delimiter, close + length, StringComparison.Ordinal);
                if (close >= 0) { code.Add((token, close + length)); i = close + length; }
                else i = end;
            }
            else i = end;
        }
        return code;
    }

    /// <summary>Позиция внутри диапазона кода — значит, разметкой это не считается.</summary>
    public static bool IsInsideCode(List<(int Start, int End)> ranges, int position)
    {
        foreach (var range in ranges)
        {
            if (position >= range.Start && position < range.End)
            {
                return true;
            }
        }

        return false;
    }
}
