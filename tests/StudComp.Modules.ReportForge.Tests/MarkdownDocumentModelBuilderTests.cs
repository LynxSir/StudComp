using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Разбор Markdown в промежуточную модель (ARCHITECTURE §10.2): каждый вид блока и начертания.
/// </summary>
public class MarkdownDocumentModelBuilderTests
{
    [Fact]
    public void Image_width_can_be_changed_and_reset_using_the_source_span()
    {
        const string source = "![Фото](<папка/фото%20%231.png>){width=200}";
        var image = Assert.IsType<ImageBlock>(Assert.Single(Build(source)));
        Assert.Equal(200, image.Width);
        Assert.Equal("папка/фото #1.png", image.PathOrBase64);
        var resized = StudComp.Core.Domain.MarkdownImageSize.SetWidth(source, image.SourceStart, image.SourceLength, 400);
        var updated = Assert.IsType<ImageBlock>(Assert.Single(Build(resized)));
        Assert.Equal(400, updated.Width);
        var automatic = StudComp.Core.Domain.MarkdownImageSize.SetWidth(resized, updated.SourceStart, updated.SourceLength, null);
        Assert.Null(Assert.IsType<ImageBlock>(Assert.Single(Build(automatic))).Width);
    }

    private static IReadOnlyList<IReportBlock> Build(string markdown) =>
        DocxTestHelpers.Builder.Build(markdown).Blocks;

    [Theory]
    [InlineData("# Первый", 1, "Первый")]
    [InlineData("## Второй", 2, "Второй")]
    [InlineData("### Третий уровень", 3, "Третий уровень")]
    public void Headings_keep_level_and_text(string markdown, int level, string text)
    {
        var heading = Assert.IsType<HeadingBlock>(Assert.Single(Build(markdown)));

        Assert.Equal(level, heading.Level);
        Assert.Equal(text, heading.Text);
    }

    [Theory]
    [InlineData("**жирный**", InlineStyle.Bold)]
    [InlineData("*курсив*", InlineStyle.Italic)]
    [InlineData("_курсив_", InlineStyle.Italic)]
    [InlineData("~~зачёркнуто~~", InlineStyle.Strikethrough)]
    [InlineData("`код`", InlineStyle.Code)]
    public void Inline_markup_becomes_a_styled_run(string markdown, InlineStyle expected)
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build(markdown)));
        var run = Assert.Single(paragraph.Runs);

        Assert.Equal(expected, run.Style);
    }

    [Fact]
    public void Nested_markup_combines_styles()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("**жирный *и курсив***")));

        Assert.Contains(paragraph.Runs, run => run.Style == (InlineStyle.Bold | InlineStyle.Italic));
    }

    [Fact]
    public void Adjacent_text_with_the_same_style_collapses_into_one_run()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("просто длинный текст абзаца")));

        Assert.Single(paragraph.Runs);
        Assert.Equal("просто длинный текст абзаца", paragraph.Runs[0].Text);
    }

    [Fact]
    public void Links_carry_their_url()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("см. [сайт](https://example.com)")));
        var link = Assert.Single(paragraph.Runs, run => run.Hyperlink is not null);

        Assert.Equal("https://example.com", link.Hyperlink);
        Assert.Equal("сайт", link.Text);
    }

    [Fact]
    public void Unordered_and_ordered_lists_are_distinguished()
    {
        var unordered = Assert.IsType<ListBlock>(Assert.Single(Build("- раз\n- два")));
        var ordered = Assert.IsType<ListBlock>(Assert.Single(Build("1. раз\n2. два")));

        Assert.False(unordered.Ordered);
        Assert.True(ordered.Ordered);
        Assert.Equal(2, unordered.Items.Count);
    }

    [Fact]
    public void Nested_list_lives_inside_its_parent_item()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Build("- верхний\n  - вложенный")));
        var item = Assert.Single(list.Items);

        Assert.Contains(item.Blocks, block => block is ListBlock);
    }

    [Fact]
    public void Table_keeps_headers_and_rows()
    {
        var markdown = """
            | Предмет | Оценка |
            |---------|--------|
            | Матан   | 5      |
            | Физика  | 4      |
            """;

        var table = Assert.IsType<TableBlock>(Assert.Single(Build(markdown)));

        Assert.Equal(["Предмет", "Оценка"], table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["Матан", "5"], table.Rows[0]);
        Assert.Null(table.Caption);
    }

    [Theory]
    [InlineData("Таблица: Итоги семестра")]
    [InlineData("Таблица — Итоги семестра")]
    [InlineData("таблица: Итоги семестра")]
    public void Caption_line_before_a_table_becomes_its_caption(string captionLine)
    {
        var markdown = $"""
            {captionLine}

            | А | Б |
            |---|---|
            | 1 | 2 |
            """;

        var blocks = Build(markdown);
        var table = Assert.IsType<TableBlock>(Assert.Single(blocks));

        Assert.Equal("Итоги семестра", table.Caption);
    }

    [Fact]
    public void Ordinary_paragraph_before_a_table_survives()
    {
        var markdown = """
            Просто вводный абзац.

            | А | Б |
            |---|---|
            | 1 | 2 |
            """;

        var blocks = Build(markdown);

        Assert.Equal(2, blocks.Count);
        Assert.IsType<ParagraphBlock>(blocks[0]);
        Assert.Null(Assert.IsType<TableBlock>(blocks[1]).Caption);
    }

    [Fact]
    public void Fenced_code_keeps_language_and_text()
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(Build("```csharp\nvar x = 1;\nvar y = 2;\n```")));

        Assert.Equal("csharp", code.Language);
        Assert.Equal("var x = 1;\nvar y = 2;", code.Code);
    }

    [Fact]
    public void Fenced_code_without_language_reports_null()
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(Build("```\nтекст\n```")));

        Assert.Null(code.Language);
    }

    [Fact]
    public void Image_takes_its_caption_from_the_title_then_the_alt_text()
    {
        var withTitle = Assert.IsType<ImageBlock>(Assert.Single(Build("![альт](a.png \"Заголовок\")")));
        var withAlt = Assert.IsType<ImageBlock>(Assert.Single(Build("![альт](a.png)")));

        Assert.Equal("Заголовок", withTitle.Caption);
        Assert.Equal("альт", withAlt.Caption);
        Assert.Equal("a.png", withAlt.PathOrBase64);
    }

    [Fact]
    public void An_image_path_wrapped_in_angle_brackets_keeps_its_spaces()
    {
        // CommonMark: путь ссылки без <...> не может содержать пробел — Markdig иначе не распознаёт
        // вставку картинкой вовсе (Phase 13.7, new_addons.md §12, найдено на реальном запуске:
        // подпапка предмета почти всегда содержит пробел в названии).
        var image = Assert.IsType<ImageBlock>(
            Assert.Single(Build("![Рисунок](<Мат. основы теории систем/Рисунки/a.png>)")));

        Assert.Equal("Мат. основы теории систем/Рисунки/a.png", image.PathOrBase64);
    }

    [Fact]
    public void Text_around_an_image_is_split_into_separate_paragraphs()
    {
        var blocks = Build("до ![карт](a.png) после");

        Assert.Collection(
            blocks,
            block => Assert.Equal("до ", Assert.IsType<ParagraphBlock>(block).Runs[0].Text),
            block => Assert.IsType<ImageBlock>(block),
            block => Assert.Equal(" после", Assert.IsType<ParagraphBlock>(block).Runs[0].Text));
    }

    [Fact]
    public void Quote_becomes_italic_paragraphs()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("> цитата")));

        Assert.All(paragraph.Runs, run => Assert.True(run.Style.HasFlag(InlineStyle.Italic)));
    }

    [Fact]
    public void Empty_markdown_yields_an_empty_document()
    {
        Assert.Empty(Build(string.Empty));
        Assert.Empty(Build("   \n\n  "));
    }

    [Fact]
    public void Thematic_break_is_dropped()
    {
        Assert.Empty(Build("---"));
    }

    // ── Формулы (new_addons.md §11.1) ───────────────────────────────────────────────────
    // До Phase 13.6 MathInline терялся в разборе, и формула не доезжала ни до предпросмотра
    // заметки, ни до отчёта: в тексте оставалась пустота.

    [Fact]
    public void Inline_formula_becomes_a_math_run_carrying_its_source()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("энергия $E = mc^2$ покоя")));

        var math = Assert.Single(paragraph.Runs, run => run.Style.HasFlag(InlineStyle.Math));
        Assert.Equal("E = mc^2", math.Text);
    }

    [Fact]
    public void Display_formula_becomes_its_own_paragraph_and_not_a_code_listing()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build(@"$$\int_a^b f(x)dx$$")));

        var run = Assert.Single(paragraph.Runs);
        Assert.True(run.Style.HasFlag(InlineStyle.Math));
        Assert.Equal(@"\int_a^b f(x)dx", run.Text);
    }

    [Fact]
    public void Formula_is_not_glued_to_the_surrounding_text()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("до $x^2$ после")));

        Assert.Collection(
            paragraph.Runs,
            run => Assert.Equal("до ", run.Text),
            run => Assert.Equal("x^2", run.Text),
            run => Assert.Equal(" после", run.Text));
    }

    [Fact]
    public void Formula_inside_a_table_cell_survives_as_text()
    {
        var table = Assert.IsType<TableBlock>(Assert.Single(Build(
            """
            | Величина | Формула |
            | --- | --- |
            | Энергия | $E = mc^2$ |
            """)));

        Assert.Equal("E = mc^2", table.Rows[0][1]);
    }

    [Fact]
    public void A_lone_dollar_sign_is_still_plain_text()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("цена 100$ за штуку")));

        Assert.DoesNotContain(paragraph.Runs, run => run.Style.HasFlag(InlineStyle.Math));
    }

    // ── Переносы строк (new_addons.md §11.7) ────────────────────────────────────────────

    [Fact]
    public void Single_enter_becomes_a_soft_break_instead_of_a_space()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("первая\nвторая")));

        Assert.Collection(
            paragraph.Runs,
            run => Assert.Equal("первая", run.Text),
            run => Assert.Equal(InlineBreak.Soft, run.Break),
            run => Assert.Equal("вторая", run.Text));
    }

    [Fact]
    public void Two_trailing_spaces_give_a_hard_break()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("первая  \nвторая")));

        Assert.Contains(paragraph.Runs, run => run.Break == InlineBreak.Hard);
    }

    [Fact]
    public void Break_runs_carry_no_text_and_keep_the_surrounding_style()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Build("**жирная\nстрока**")));

        var lineBreak = Assert.Single(paragraph.Runs, run => run.Break != InlineBreak.None);
        Assert.Equal(string.Empty, lineBreak.Text);
        Assert.True(lineBreak.Style.HasFlag(InlineStyle.Bold));
    }

    [Fact]
    public void A_blank_line_still_starts_a_new_paragraph()
    {
        var blocks = Build("первый\n\nвторой");

        Assert.Equal(2, blocks.Count);
        Assert.DoesNotContain(
            Assert.IsType<ParagraphBlock>(blocks[0]).Runs,
            run => run.Break != InlineBreak.None);
    }
}
