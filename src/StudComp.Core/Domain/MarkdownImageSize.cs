using System.Text.RegularExpressions;

namespace StudComp.Core.Domain;

/// <summary>
/// Ширина картинки в размеченном тексте: generic-атрибут <c>{width=N}</c> сразу за ссылкой.
/// </summary>
public static partial class MarkdownImageSize
{
    /// <summary>
    /// Задать или снять ширину картинки, ссылка которой занимает <paramref name="length"/> символов
    /// от <paramref name="start"/>. Прочие атрибуты блока сохраняются, порядок — «остальные, потом
    /// ширина», а лишние пробелы внутри скобок схлопываются: иначе повторные изменения размера
    /// накапливали бы их одно за другим.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Ширина вне 32…2400. Вызывающему следует зажимать значение заранее —
    /// это делает <see cref="MarkdownImageEditing.SetWidth"/>.
    /// </exception>
    public static string SetWidth(string markdown, int start, int length, int? width)
    {
        if (start < 0 || length <= 0 || start + length > markdown.Length)
            return markdown;
        if (width is not null and (< 32 or > 2400))
            throw new ArgumentOutOfRangeException(nameof(width));

        var end = start + length;
        // Спан ссылки у Markdig кончается до generic-атрибутов, поэтому блок ищется вплотную за ним.
        var attributes = MarkdownLocalImages.AttributeBlock().Match(markdown, end);
        var value = attributes.Success && attributes.Index == end ? attributes.Value : string.Empty;

        var others = Whitespace().Replace(
            WidthAttribute().Replace(value.Trim('{', '}'), string.Empty), " ").Trim();
        var inner = width is { } pixels
            ? others.Length == 0 ? $"width={pixels}" : $"{others} width={pixels}"
            : others;
        var remaining = inner.Length == 0 ? string.Empty : $"{{{inner}}}";

        return markdown[..end] + remaining + markdown[(end + value.Length)..];
    }

    [GeneratedRegex("\\bwidth\\s*=\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s}]+)\\s*", RegexOptions.IgnoreCase)]
    private static partial Regex WidthAttribute();

    /// <summary>Любая непрерывная последовательность пробелов внутри блока атрибутов.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
