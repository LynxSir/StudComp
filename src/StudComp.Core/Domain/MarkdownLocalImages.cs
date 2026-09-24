using System.Text.RegularExpressions;

namespace StudComp.Core.Domain;

/// <summary>Local inline image links, including the angle-bracket syntax used by the editor.</summary>
public static partial class MarkdownLocalImages
{
    public static IEnumerable<string> Paths(string markdown) => ImageMatches(markdown)
        .Select(x => Decode(x.Groups["angle"].Success ? x.Groups["angle"].Value : x.Groups["plain"].Value))
        .Where(IsLocal);

    public static string Rewrite(string markdown, Func<string, string> map)
    {
        foreach (var match in ImageMatches(markdown).Reverse())
        {
            var group = match.Groups["angle"].Success ? match.Groups["angle"] : match.Groups["plain"];
            var path = Decode(group.Value);
            if (!IsLocal(path)) continue;
            var mapped = map(path);
            if (mapped != path) markdown = markdown[..group.Index] + Encode(mapped) + markdown[(group.Index + group.Length)..];
        }
        return markdown;
    }

    private static IEnumerable<Match> ImageMatches(string markdown)
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
        return Links().Matches(markdown).Where(match => !code.Any(range => match.Index >= range.Start && match.Index < range.End));
    }

    public static string Encode(string path) => path.Replace('\\', '/').Replace("%", "%25").Replace("#", "%23")
        .Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");

    public static string Decode(string path) => Uri.UnescapeDataString(path);

    private static bool IsLocal(string path) => Path.IsPathRooted(path)
        || !Uri.TryCreate(path, UriKind.Absolute, out _);

    [GeneratedRegex(@"!\[(?:\\.|[^\]\\])*\]\(\s*(?:<(?<angle>[^>\r\n]+)>|(?<plain>(?:\\.|[^\s()]+|\([^()]*\))+))(?:\s+""[^""\r\n]*"")?\s*\)", RegexOptions.None, 1000)]
    private static partial Regex Links();
}
