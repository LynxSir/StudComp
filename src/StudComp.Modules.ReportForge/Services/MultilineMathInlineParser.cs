using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Renderers;

namespace StudComp.Modules.ReportForge.Services;

internal sealed class MultilineMathExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline) =>
        pipeline.InlineParsers.Insert(0, new MultilineMathInlineParser());

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }
}

/// <summary>Многострочный TeX внутри абзаца: содержимое не должно разбираться как Markdown.</summary>
internal sealed class MultilineMathInlineParser : InlineParser
{
    public MultilineMathInlineParser() => OpeningCharacters = ['$'];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var start = slice.Start;
        if (slice.PeekCharExtra(-1) == '$' || IsEscaped(slice.Text, start)) return false;
        var dollars = slice.PeekChar(1) == '$' ? 2 : 1;
        if (dollars == 2 && slice.PeekChar(2) == '$') return false;
        var previous = slice.PeekCharExtra(-1);
        if (previous != '\0' && !char.IsWhiteSpace(previous) && !char.IsPunctuation(previous)) return false;

        for (var close = start + dollars; close <= slice.End; close++)
        {
            if (slice.Text[close] != '$' || IsEscaped(slice.Text, close)) continue;
            var count = 1;
            while (close + count <= slice.End && slice.Text[close + count] == '$') count++;
            if (count != dollars) return false;
            var following = close + dollars <= slice.End ? slice.Text[close + dollars] : '\0';
            if (following != '\0' && !char.IsWhiteSpace(following) && !char.IsPunctuation(following)) return false;
            var content = new StringSlice(slice.Text, start + dollars, close - 1);
            var text = content.ToString();
            // Обычные однострочные формулы и денежные суммы оставляем штатному разборщику.
            if (!text.Contains('\\') || (!text.Contains("\\begin{", StringComparison.Ordinal)
                && !text.Contains('\n') && !text.Contains('\r'))) return false;
            processor.Inline = new MathInline
            {
                Delimiter = '$', DelimiterCount = dollars, Content = content,
                Span = new SourceSpan(processor.GetSourcePosition(start, out var line, out var column),
                    processor.GetSourcePosition(close + dollars - 1)),
                Line = line, Column = column,
            };
            slice.Start = close + dollars;
            return true;
        }
        return false;
    }

    private static bool IsEscaped(string text, int position)
    {
        var count = 0;
        while (position > 0 && text[--position] == '\\') count++;
        return count % 2 != 0;
    }
}
