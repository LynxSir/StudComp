using System.Text.RegularExpressions;

namespace StudComp.Core.Domain;

/// <summary>
/// Ссылки на локальные картинки в размеченном тексте, включая форму с угловыми скобками
/// (её ставит редактор заметки: по CommonMark путь без скобок не может содержать пробел).
/// </summary>
public static partial class MarkdownLocalImages
{
    /// <summary>Локальные пути всех картинок текста, в порядке следования.</summary>
    public static IEnumerable<string> Paths(string markdown) =>
        Tokens(markdown).Where(token => token.IsLocal).Select(token => token.Path);

    /// <summary>
    /// Границы всех вставок картинок — вместе с хвостовым блоком атрибутов <c>{width=N}</c>, который
    /// сам Markdig в спан ссылки не включает. Сканер кодовых заборов тут один и тот же, что у
    /// <see cref="Paths"/>: пример внутри <c>`…`</c> или <c>```…```</c> картинкой не считается.
    /// </summary>
    public static IReadOnlyList<MarkdownImageToken> Tokens(string? markdown)
    {
        var source = markdown ?? string.Empty;

        // Разбор зовётся на каждое движение каретки и на каждое нажатие клавиши, поэтому сначала
        // дешёвая отсечка: без «](» ссылки в тексте быть не может, и обходить кодовые заборы
        // регулярками незачем.
        if (!source.Contains("](", StringComparison.Ordinal))
        {
            return [];
        }

        var tokens = new List<MarkdownImageToken>();

        foreach (var match in ImageMatches(source))
        {
            var group = match.Groups["angle"].Success ? match.Groups["angle"] : match.Groups["plain"];
            var path = Decode(group.Value);

            // Блок атрибутов принадлежит картинке только если прилегает к ней без пробела.
            var attributes = AttributeBlock().Match(source, match.Index + match.Length);
            var tail = attributes.Success && attributes.Index == match.Index + match.Length
                ? attributes.Length
                : 0;

            tokens.Add(new MarkdownImageToken(
                match.Index, match.Length + tail, match.Length, path, IsLocal(path)));
        }

        return tokens;
    }

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
        var code = MarkdownScanner.CodeRanges(markdown);

        return Links().Matches(markdown).Where(match => !code.Any(range => match.Index >= range.Start && match.Index < range.End));
    }

    public static string Encode(string path) => path.Replace('\\', '/').Replace("%", "%25").Replace("#", "%23")
        .Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");

    public static string Decode(string path) => Uri.UnescapeDataString(path);

    private static bool IsLocal(string path) => Path.IsPathRooted(path)
        || !Uri.TryCreate(path, UriKind.Absolute, out _);

    [GeneratedRegex(@"!\[(?:\\.|[^\]\\])*\]\(\s*(?:<(?<angle>[^>\r\n]+)>|(?<plain>(?:\\.|[^\s()]+|\([^()]*\))+))(?:\s+""[^""\r\n]*"")?\s*\)", RegexOptions.None, 1000)]
    private static partial Regex Links();

    /// <summary>Блок generic-атрибутов сразу за ссылкой: <c>{width=320 #anchor}</c>.</summary>
    [GeneratedRegex(@"\G\{[^\r\n}]*\}")]
    internal static partial Regex AttributeBlock();
}
