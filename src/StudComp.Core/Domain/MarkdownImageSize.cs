using System.Text.RegularExpressions;

namespace StudComp.Core.Domain;

public static partial class MarkdownImageSize
{
    public static string SetWidth(string markdown, int start, int length, int? width)
    {
        if (start < 0 || length <= 0 || start + length > markdown.Length)
            return markdown;
        if (width is not null and (< 32 or > 2400))
            throw new ArgumentOutOfRangeException(nameof(width));

        var end = start + length;
        // Markdig's link span ends before generic attributes.
        var attributes = Attributes().Match(markdown, end);
        var value = attributes.Success && attributes.Index == end ? attributes.Value : string.Empty;
        var remaining = WidthAttribute().Replace(value, string.Empty);
        if (width is { } pixels)
            remaining = remaining.Length == 0 ? $"{{width={pixels}}}" : remaining.Insert(remaining.LastIndexOf('}'), $" width={pixels}");
        if (remaining.Trim() == "{}") remaining = string.Empty;
        return markdown[..end] + remaining + markdown[(end + value.Length)..];
    }

    [GeneratedRegex(@"\G\{[^\r\n}]*\}")]
    private static partial Regex Attributes();

    [GeneratedRegex("\\bwidth\\s*=\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s}]+)\\s*", RegexOptions.IgnoreCase)]
    private static partial Regex WidthAttribute();
}
