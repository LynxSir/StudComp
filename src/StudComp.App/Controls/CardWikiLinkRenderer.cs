using System.Windows.Documents;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;

namespace StudComp.Controls;

/// <summary>
/// Wiki-ссылки <c>[[Название]]</c> в обороте карточки (new_addons.md §7, объём Phase 12.9).
/// </summary>
/// <remarks>
/// Обёртка над <see cref="MarkdownFlowRenderer"/>, а не правка общего рендера: заметкам ссылки на
/// карточки не нужны, и подключать разбор всюду незачем. Работает уже над готовым
/// <see cref="FlowDocument"/> — ищет листовые <see cref="Run"/> с <c>[[…]]</c> внутри любых обёрток
/// начертания (<see cref="Bold"/>/<see cref="Italic"/>/<see cref="Span"/>) и режет их на текст и
/// <see cref="Hyperlink"/>. Никакого нового поля в модели данных и никакой миграции — синтаксис живёт
/// только поверх текста, как пропуски <see cref="ClozeParser"/>.
/// </remarks>
public static class CardWikiLinkRenderer
{
    /// <summary>Собрать документ и превратить встреченные ссылки в кликабельные.</summary>
    public static FlowDocument Render(IMarkdownDocumentModelBuilder builder, string? back, Action<string> onLinkClick)
    {
        var document = MarkdownFlowRenderer.Render(builder, back);

        foreach (var block in document.Blocks.ToList())
        {
            RewriteBlock(block, onLinkClick);
        }

        return document;
    }

    private static void RewriteBlock(Block block, Action<string> onLinkClick)
    {
        switch (block)
        {
            case Paragraph paragraph:
                RewriteInlines(paragraph.Inlines, onLinkClick);
                break;

            case List list:
                foreach (var item in list.ListItems)
                {
                    foreach (var child in item.Blocks.ToList())
                    {
                        RewriteBlock(child, onLinkClick);
                    }
                }

                break;

            case Table table:
                foreach (var group in table.RowGroups)
                {
                    foreach (var row in group.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var child in cell.Blocks.ToList())
                            {
                                RewriteBlock(child, onLinkClick);
                            }
                        }
                    }
                }

                break;
        }
    }

    private static void RewriteInlines(InlineCollection inlines, Action<string> onLinkClick)
    {
        foreach (var inline in inlines.ToList())
        {
            switch (inline)
            {
                case Run run when WikiLinkParser.ContainsLinks(run.Text):
                    ReplaceRunWithLinks(inlines, run, onLinkClick);
                    break;

                // Bold/Italic/Underline и обычный Span — все наследуют Span и несут вложенные инлайны.
                case Span span:
                    RewriteInlines(span.Inlines, onLinkClick);
                    break;
            }
        }
    }

    private static void ReplaceRunWithLinks(InlineCollection inlines, Run run, Action<string> onLinkClick)
    {
        var parsed = WikiLinkParser.Parse(run.Text);
        Inline anchor = run;

        foreach (var segment in parsed.Segments)
        {
            Inline replacement = segment.Kind == WikiLinkSegmentKind.Link
                ? BuildLink(segment.Target!, onLinkClick)
                : new Run(segment.Text);

            inlines.InsertAfter(anchor, replacement);
            anchor = replacement;
        }

        inlines.Remove(run);
    }

    private static Hyperlink BuildLink(string target, Action<string> onLinkClick)
    {
        var link = new Hyperlink(new Run(target)) { ToolTip = target };
        link.Click += (_, _) => onLinkClick(target);
        return link;
    }
}
