using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax.Inlines;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using Md = Markdig.Syntax;

namespace StudComp.Modules.ReportForge.Services;

/// <summary>
/// Markdig AST → <see cref="ReportDocumentModel"/> (ARCHITECTURE §10.2). Промежуточная модель
/// обязательна: она развязывает разбор Markdown и ГОСТ-рендеринг (ADR §16.4).
/// </summary>
/// <remarks>
/// Имена блоков Markdig и наши совпадают (<c>HeadingBlock</c>, <c>ListBlock</c>, <c>CodeBlock</c>,
/// <c>ParagraphBlock</c>), поэтому Markdig подключён под алиасом <c>Md</c>.
/// </remarks>
internal sealed partial class MarkdownDocumentModelBuilder : IMarkdownDocumentModelBuilder
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public ReportDocumentModel Build(string markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        return new ReportDocumentModel(TitlePage: null, Blocks: ReadBlocks(document), GenerateTableOfContents: false);
    }

    /// <summary>
    /// Подпись таблицы: строка вида «Таблица: Название» (или через тире) непосредственно перед таблицей.
    /// Слово «Таблица» и номер ставит рендерер, здесь забирается только название (ADR §16.31).
    /// </summary>
    [GeneratedRegex(@"^\s*Таблица\s*[:\-—–]\s*(?<name>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TableCaptionPattern();

    private static List<IReportBlock> ReadBlocks(Md.ContainerBlock container)
    {
        var blocks = new List<IReportBlock>();

        foreach (var child in container)
        {
            switch (child)
            {
                case Md.HeadingBlock heading:
                    blocks.Add(new HeadingBlock(heading.Level, ToPlainText(heading.Inline)));
                    break;

                case Md.ParagraphBlock paragraph:
                    AppendParagraph(paragraph, blocks);
                    break;

                case Md.ListBlock list:
                    blocks.Add(ReadList(list));
                    break;

                case Table table:
                    blocks.Add(ReadTable(table, blocks));
                    break;

                case MathBlock math:
                    // Выключная формула $$…$$ — отдельный абзац из одного куска-формулы. Ветка идёт
                    // выше листинга намеренно: в Markdig MathBlock наследует FencedCodeBlock, и до
                    // этой фазы формула уезжала в отчёт обычным листингом.
                    blocks.Add(new ParagraphBlock([new InlineRun(ReadLines(math), InlineStyle.Math)]));
                    break;

                case Md.CodeBlock code:
                    blocks.Add(new CodeBlock(
                        (code as Md.FencedCodeBlock)?.Info is { Length: > 0 } info ? info : null,
                        ReadLines(code)));
                    break;

                case Md.QuoteBlock quote:
                    // Цитаты в отчёте по ГОСТ своего оформления не имеют — разворачиваем в курсивные абзацы.
                    blocks.AddRange(ReadBlocks(quote).Select(Italicize));
                    break;

                case Md.ContainerBlock nested:
                    // Всё прочее контейнерное (блоки расширений Markdig) отдаёт своё содержимое как есть.
                    blocks.AddRange(ReadBlocks(nested));
                    break;

                // Горизонтальные линейки, определения ссылок и HTML-блоки в .docx не переносятся.
            }
        }

        return blocks;
    }

    /// <summary>
    /// Абзац может содержать изображения вперемешку с текстом, а <see cref="ImageBlock"/> — блок,
    /// не инлайн. Поэтому накопленный текст выпускается отдельным абзацем, затем идёт картинка.
    /// </summary>
    private static void AppendParagraph(Md.ParagraphBlock paragraph, List<IReportBlock> blocks)
    {
        var runs = new List<InlineRun>();
        AppendInlines(paragraph.Inline, InlineStyle.None, hyperlink: null, runs, blocks);
        FlushRuns(runs, blocks);
    }

    private static void FlushRuns(List<InlineRun> runs, List<IReportBlock> blocks)
    {
        if (runs.Count == 0)
        {
            return;
        }

        blocks.Add(new ParagraphBlock([.. runs]));
        runs.Clear();
    }

    private static void AppendInlines(
        ContainerInline? container,
        InlineStyle style,
        string? hyperlink,
        List<InlineRun> runs,
        List<IReportBlock> blocks)
    {
        for (var inline = container?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendText(runs, literal.Content.ToString(), style, hyperlink);
                    break;

                case CodeInline code:
                    AppendText(runs, code.Content, style | InlineStyle.Code, hyperlink);
                    break;

                case EmphasisInline emphasis:
                    AppendInlines(emphasis, style | ToStyle(emphasis), hyperlink, runs, blocks);
                    break;

                case LinkInline { IsImage: true } image:
                    // Картинка обрывает абзац: накопленное уходит своим абзацем, картинка — отдельным блоком.
                    FlushRuns(runs, blocks);
                    blocks.Add(new ImageBlock(
                        MarkdownLocalImages.Decode(image.Url ?? string.Empty),
                        Coalesce(image.Title, ToPlainText(image)))
                    {
                        Width = int.TryParse(image.GetAttributes().Properties?.FirstOrDefault(x => x.Key == "width").Value,
                            out var width) && width is >= 32 and <= 2400 ? width : null,
                        SourceStart = image.Span.Start,
                        SourceLength = image.Span.Length,
                    });
                    break;

                case LinkInline link:
                    AppendInlines(link, style, link.Url, runs, blocks);
                    break;

                case AutolinkInline autolink:
                    AppendText(runs, autolink.Url, style, autolink.Url);
                    break;

                case HtmlEntityInline entity:
                    AppendText(runs, entity.Transcoded.ToString(), style, hyperlink);
                    break;

                case MathInline math:
                    // Исходник формулы едет дальше как есть — верстает его уже рендерер
                    // (new_addons.md §11.1). MathInline — LeafInline, поэтому ветка ContainerInline
                    // ниже его не ловила и формула пропадала бесследно.
                    AppendText(runs, math.Content.ToString(), style | InlineStyle.Math, hyperlink);
                    break;

                case LineBreakInline lineBreak:
                    // Перенос доезжает до модели отдельным куском, а не пробелом: иначе одиночный
                    // Enter пропадал в предпросмотре заметки (new_addons.md §11.7).
                    runs.Add(new InlineRun(
                        string.Empty,
                        style,
                        hyperlink,
                        lineBreak.IsHard ? InlineBreak.Hard : InlineBreak.Soft));
                    break;

                case ContainerInline nested:
                    AppendInlines(nested, style, hyperlink, runs, blocks);
                    break;

                // Сырой HTML внутри строки в .docx не переносится.
            }
        }
    }

    private static void AppendText(List<InlineRun> runs, string? text, InlineStyle style, string? hyperlink)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Соседние куски с одинаковым оформлением склеиваем — иначе на абзац выйдут десятки w:r.
        // Перенос и формулу склеивать нельзя: у первого нет текста, у второй в тексте лежит
        // исходник TeX, и дописывание к нему исказило бы формулу.
        if (runs.Count > 0
            && runs[^1] is { } last
            && last.Style == style
            && last.Hyperlink == hyperlink
            && last.Break == InlineBreak.None
            && !style.HasFlag(InlineStyle.Math))
        {
            runs[^1] = last with { Text = last.Text + text };
            return;
        }

        runs.Add(new InlineRun(text, style, hyperlink));
    }

    private static InlineStyle ToStyle(EmphasisInline emphasis) => emphasis.DelimiterChar switch
    {
        '~' => emphasis.DelimiterCount >= 2 ? InlineStyle.Strikethrough : InlineStyle.None,
        '+' => InlineStyle.Underline,
        _ => emphasis.DelimiterCount >= 2 ? InlineStyle.Bold : InlineStyle.Italic,
    };

    private static ListBlock ReadList(Md.ListBlock list)
    {
        var items = new List<ReportDocumentModel>();

        foreach (var child in list)
        {
            if (child is Md.ListItemBlock item)
            {
                items.Add(new ReportDocumentModel(null, ReadBlocks(item), GenerateTableOfContents: false));
            }
        }

        return new ListBlock(list.IsOrdered, items);
    }

    /// <summary>
    /// Собирает таблицу и забирает подпись из предыдущего абзаца, если он ею и является: абзац
    /// изымается из <paramref name="blocks"/>, чтобы не напечататься дважды.
    /// </summary>
    private static TableBlock ReadTable(Table table, List<IReportBlock> blocks)
    {
        var headers = new List<string>();
        var rows = new List<IReadOnlyList<string>>();

        foreach (var child in table)
        {
            if (child is not TableRow row)
            {
                continue;
            }

            var cells = new List<string>();
            foreach (var cellBlock in row)
            {
                cells.Add(cellBlock is TableCell cell ? ToPlainText(cell) : string.Empty);
            }

            if (row.IsHeader && headers.Count == 0)
            {
                headers.AddRange(cells);
            }
            else
            {
                rows.Add(cells);
            }
        }

        return new TableBlock(headers, rows, TakeCaption(blocks));
    }

    private static string? TakeCaption(List<IReportBlock> blocks)
    {
        if (blocks.Count == 0 || blocks[^1] is not ParagraphBlock paragraph)
        {
            return null;
        }

        var text = string.Concat(paragraph.Runs.Select(run => run.Text));
        var match = TableCaptionPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        blocks.RemoveAt(blocks.Count - 1);
        return match.Groups["name"].Value;
    }

    private static IReportBlock Italicize(IReportBlock block) => block is ParagraphBlock paragraph
        ? new ParagraphBlock([.. paragraph.Runs.Select(run => run with { Style = run.Style | InlineStyle.Italic })])
        : block;

    /// <summary>Сырые строки блока: годится и листингу, и выключной формуле — у обоих это <c>LeafBlock</c>.</summary>
    private static string ReadLines(Md.LeafBlock code)
    {
        var builder = new StringBuilder();
        var lines = code.Lines.Lines;

        for (var i = 0; i < code.Lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(lines[i].Slice.ToString());
        }

        return builder.ToString();
    }

    /// <summary>Плоский текст контейнера — для ячеек таблиц, заголовков и alt-текста картинок.</summary>
    private static string ToPlainText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendPlainText(container, builder);
        return builder.ToString().Trim();
    }

    private static string ToPlainText(Md.ContainerBlock container)
    {
        var builder = new StringBuilder();

        foreach (var child in container)
        {
            switch (child)
            {
                case Md.LeafBlock { Inline: { } inline }:
                    AppendSeparator(builder);
                    AppendPlainText(inline, builder);
                    break;

                case Md.ContainerBlock nested:
                    AppendSeparator(builder);
                    builder.Append(ToPlainText(nested));
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }
    }

    private static void AppendPlainText(ContainerInline container, StringBuilder builder)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.AsSpan());
                    break;

                case CodeInline code:
                    builder.Append(code.Content);
                    break;

                case MathInline math:
                    // В ячейке таблицы вёрстки формул нет, но исчезать её содержимое не должно.
                    builder.Append(math.Content.AsSpan());
                    break;

                case AutolinkInline autolink:
                    builder.Append(autolink.Url);
                    break;

                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded.AsSpan());
                    break;

                case LineBreakInline:
                    builder.Append(' ');
                    break;

                case ContainerInline nested:
                    AppendPlainText(nested, builder);
                    break;
            }
        }
    }

    private static string? Coalesce(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first)
            ? string.IsNullOrWhiteSpace(second) ? null : second
            : first;
}
