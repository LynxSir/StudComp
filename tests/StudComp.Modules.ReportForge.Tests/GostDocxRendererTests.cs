using System.Xml.Linq;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Modules.ReportForge.Services.Docx;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Рендерер: схемная валидация пакета (обязательный шаг, ARCHITECTURE §12) и проверка того, что
/// параметры профиля действительно доехали до <c>document.xml</c>/<c>styles.xml</c>/<c>numbering.xml</c>.
/// </summary>
public class GostDocxRendererTests
{
    private static readonly XNamespace W = DocxTestHelpers.W;

    private static TitlePageInfo SampleTitlePage => new(
        University: "Технологический университет",
        Faculty: "Институт информационных технологий",
        Department: "Программной инженерии",
        WorkType: "Отчёт по лабораторной работе",
        SubjectName: "Математический анализ",
        StudentName: "Иванов И. И.",
        StudentGroup: "ИВТ-201",
        SupervisorName: "Петров П. П.",
        City: "Новосибирск",
        Year: 2026);

    [Fact]
    public async Task Full_document_passes_schema_validation()
    {
        var markdown = await File.ReadAllTextAsync(Path.Combine("Fixtures", "sample-report.md"));
        var model = DocxTestHelpers.Builder.Build(markdown) with
        {
            TitlePage = SampleTitlePage,
            GenerateTableOfContents = true,
        };

        var docx = await DocxTestHelpers.RenderAsync(WithInlineImage(model));
        var errors = DocxTestHelpers.Validate(docx);

        Assert.True(errors.Count == 0, DocxTestHelpers.Describe(errors));
    }

    [Fact]
    public async Task Page_size_and_margins_come_from_the_profile()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел");
        var section = DocxTestHelpers.ReadDocument(docx).Descendants(W + "sectPr").Single();

        var size = section.Element(W + "pgSz")!;
        Assert.Equal("11906", size.Attribute(W + "w")!.Value);
        Assert.Equal("16838", size.Attribute(W + "h")!.Value);

        // 30/15/20/20 мм в twip: 1440 / 25.4 на миллиметр.
        var margin = section.Element(W + "pgMar")!;
        Assert.Equal("1701", margin.Attribute(W + "left")!.Value);
        Assert.Equal("850", margin.Attribute(W + "right")!.Value);
        Assert.Equal("1134", margin.Attribute(W + "top")!.Value);
        Assert.Equal("1134", margin.Attribute(W + "bottom")!.Value);
    }

    [Fact]
    public async Task Body_font_spacing_and_indent_come_from_the_profile()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("Обычный абзац.");
        var defaults = DocxTestHelpers.ReadStyles(docx).Descendants(W + "docDefaults").Single();

        Assert.Equal(
            "Times New Roman",
            defaults.Descendants(W + "rFonts").First().Attribute(W + "ascii")!.Value);

        // 14 пт хранятся в половинах пункта, интервал 1.5 — в двадцатых долях строки.
        Assert.Equal("28", defaults.Descendants(W + "sz").First().Attribute(W + "val")!.Value);
        Assert.Equal("360", defaults.Descendants(W + "spacing").First().Attribute(W + "line")!.Value);
        Assert.Equal("709", defaults.Descendants(W + "ind").First().Attribute(W + "firstLine")!.Value);
        Assert.Equal("both", defaults.Descendants(W + "jc").First().Attribute(W + "val")!.Value);
    }

    [Fact]
    public async Task Heading_styles_are_named_for_word_and_carry_outline_levels()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел\n\n## Подраздел");
        var styles = DocxTestHelpers.ReadStyles(docx);

        var first = FindStyle(styles, "Heading1");
        Assert.Equal("heading 1", first.Element(W + "name")!.Attribute(W + "val")!.Value);
        Assert.NotNull(first.Descendants(W + "outlineLvl").SingleOrDefault());
        Assert.Equal("0", first.Descendants(W + "outlineLvl").Single().Attribute(W + "val")!.Value);

        // Разделы 1-го уровня начинаются с новой страницы, вложенные — нет.
        Assert.NotNull(first.Descendants(W + "pageBreakBefore").SingleOrDefault());
        Assert.Null(FindStyle(styles, "Heading2").Descendants(W + "pageBreakBefore").SingleOrDefault());
    }

    [Fact]
    public async Task Headings_reference_their_style_in_the_body()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел");
        var styleIds = DocxTestHelpers.ReadDocument(docx)
            .Descendants(W + "pStyle")
            .Select(element => element.Attribute(W + "val")!.Value);

        Assert.Contains("Heading1", styleIds);
    }

    [Fact]
    public async Task Bullet_marker_from_the_profile_reaches_the_numbering_part()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("- пункт");
        var markers = DocxTestHelpers.ReadNumbering(docx)
            .Descendants(W + "lvlText")
            .Select(element => element.Attribute(W + "val")!.Value);

        Assert.Contains("–", markers);
    }

    [Fact]
    public async Task Custom_bullet_marker_is_honoured()
    {
        var profile = DocxTestHelpers.DefaultProfile with { BulletMarker = "•" };
        var docx = await DocxTestHelpers.RenderMarkdownAsync("- пункт", profile: profile);

        var markers = DocxTestHelpers.ReadNumbering(docx)
            .Descendants(W + "lvlText")
            .Select(element => element.Attribute(W + "val")!.Value)
            .ToArray();

        Assert.Contains("•", markers);
        Assert.DoesNotContain("–", markers);
    }

    [Fact]
    public async Task List_items_are_numbered_and_nesting_raises_the_level()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("- верхний\n  - вложенный");
        var levels = DocxTestHelpers.ReadDocument(docx)
            .Descendants(W + "numPr")
            .Select(element => element.Element(W + "ilvl")!.Attribute(W + "val")!.Value)
            .ToArray();

        Assert.Equal(["0", "1"], levels);
    }

    [Fact]
    public async Task Table_gets_a_numbered_caption_with_its_name()
    {
        var markdown = """
            Таблица: Итоги семестра

            | Предмет | Оценка |
            |---------|--------|
            | Матан   | 5      |
            """;

        var paragraphs = DocxTestHelpers.ReadParagraphs(await DocxTestHelpers.RenderMarkdownAsync(markdown));

        Assert.Contains("Таблица 1 — Итоги семестра", paragraphs);
    }

    [Fact]
    public async Task Table_without_a_caption_still_gets_a_number()
    {
        var markdown = """
            | А | Б |
            |---|---|
            | 1 | 2 |
            """;

        Assert.Contains("Таблица 1", DocxTestHelpers.ReadParagraphs(await DocxTestHelpers.RenderMarkdownAsync(markdown)));
    }

    [Fact]
    public async Task Table_numbering_is_continuous_across_the_report()
    {
        var markdown = """
            | А |
            |---|
            | 1 |

            текст между таблицами

            | Б |
            |---|
            | 2 |
            """;

        var paragraphs = DocxTestHelpers.ReadParagraphs(await DocxTestHelpers.RenderMarkdownAsync(markdown));

        Assert.Contains("Таблица 1", paragraphs);
        Assert.Contains("Таблица 2", paragraphs);
    }

    [Fact]
    public async Task Table_header_row_repeats_on_every_page()
    {
        var markdown = """
            | А | Б |
            |---|---|
            | 1 | 2 |
            """;

        var docx = await DocxTestHelpers.RenderMarkdownAsync(markdown);

        Assert.Single(DocxTestHelpers.ReadDocument(docx).Descendants(W + "tblHeader"));
    }

    [Fact]
    public async Task Image_is_embedded_with_a_numbered_caption()
    {
        using var folder = new TempFolder("rf-image");
        var imagePath = TestImages.Write(folder.Combine("pipeline.png"));

        var model = new ReportDocumentModel(
            null,
            [new ImageBlock(imagePath, "Схема пайплайна")],
            GenerateTableOfContents: false);

        var docx = await DocxTestHelpers.RenderAsync(model);

        Assert.Empty(DocxTestHelpers.Validate(docx));
        Assert.Contains("Рисунок 1 — Схема пайплайна", DocxTestHelpers.ReadParagraphs(docx));
        Assert.Single(DocxTestHelpers.ReadDocument(docx).Descendants(W + "drawing"));
    }

    [Fact]
    public async Task Image_wider_than_the_text_block_is_scaled_down_proportionally()
    {
        // Картинка 120×80 в EMU при 96 dpi уже уже строки, поэтому берём её как есть.
        var model = new ReportDocumentModel(
            null,
            [new ImageBlock(TestImages.SampleDataUri, null)],
            GenerateTableOfContents: false);

        var docx = await DocxTestHelpers.RenderAsync(model);
        var extent = DocxTestHelpers.ReadDocument(docx)
            .Descendants(XName.Get("extent", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"))
            .Single();

        var width = long.Parse(extent.Attribute("cx")!.Value);
        var height = long.Parse(extent.Attribute("cy")!.Value);

        Assert.Equal(TestImages.SampleWidth * 914400L / 96, width);
        Assert.Equal(TestImages.SampleHeight * 914400L / 96, height);
    }

    [Fact]
    public async Task Image_taller_than_the_page_is_capped_by_the_usable_height()
    {
        // 600×6000 px при 96 dpi — колонна высотой около 158 см, много выше полосы набора.
        var model = new ReportDocumentModel(
            null,
            [new ImageBlock("data:image/png;base64," + Convert.ToBase64String(TestImages.BuildPng(600, 6000)), null)],
            GenerateTableOfContents: false);

        var docx = await DocxTestHelpers.RenderAsync(model);
        Assert.Empty(DocxTestHelpers.Validate(docx));

        var extent = DocxTestHelpers.ReadDocument(docx)
            .Descendants(XName.Get("extent", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"))
            .Single();
        var width = long.Parse(extent.Attribute("cx")!.Value);
        var height = long.Parse(extent.Attribute("cy")!.Value);

        var naturalHeight = 6000L * 914400L / 96L;
        var pageTextHeight = DocxUnits.TwipsToEmu(SectionFactory.TextHeightTwips(DocxTestHelpers.DefaultProfile));

        Assert.True(height < naturalHeight, "высокое изображение должно быть ужато");
        Assert.True(height <= pageTextHeight, "картинка не должна быть выше полосы набора");
        Assert.True(width < 600L * 914400L / 96L, "ширина ужимается вместе с высотой, пропорции сохраняются");
    }

    [Fact]
    public async Task Image_density_from_png_phys_shrinks_the_extent()
    {
        // 300 px при 150 dpi = ровно 2 дюйма, влезает в ширину строки без ужимания.
        var bytes = TestImages.BuildPng(300, 200, TestImages.PixelsPerMetreForDpi(150));
        var model = new ReportDocumentModel(
            null,
            [new ImageBlock("data:image/png;base64," + Convert.ToBase64String(bytes), null)],
            GenerateTableOfContents: false);

        var extent = DocxTestHelpers.ReadDocument(await DocxTestHelpers.RenderAsync(model))
            .Descendants(XName.Get("extent", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"))
            .Single();

        Assert.Equal(300L * 914400L / 150L, long.Parse(extent.Attribute("cx")!.Value));
        Assert.Equal(200L * 914400L / 150L, long.Parse(extent.Attribute("cy")!.Value));
    }

    [Fact]
    public async Task Table_columns_are_proportional_to_content_length()
    {
        var markdown = """
            | № | Очень длинное описание пункта требований | X |
            |---|-----------------------------------------|---|
            | 1 | текст                                   | y |
            """;

        var grid = DocxTestHelpers.ReadDocument(await DocxTestHelpers.RenderMarkdownAsync(markdown))
            .Descendants(W + "tblGrid")
            .Single();
        var widths = grid.Elements(W + "gridCol")
            .Select(column => int.Parse(column.Attribute(W + "w")!.Value))
            .ToArray();

        Assert.Equal(3, widths.Length);
        Assert.True(widths[1] > widths[0] && widths[1] > widths[2], "широкая колонка должна быть шире узких");

        // Сумма ширин колонок = полезная ширина строки (лист A4 минус поля 30/15 мм).
        Assert.Equal(SectionFactory.TextWidthTwips(DocxTestHelpers.DefaultProfile), widths.Sum());
    }

    [Fact]
    public async Task Code_block_style_follows_the_profile()
    {
        var profile = DocxTestHelpers.DefaultProfile with
        {
            CodeBlock = new CodeBlockStyleRule("Fira Code", 0d, Boxed: false, BackgroundHex: "EEEEEE"),
        };

        var style = FindStyle(
            DocxTestHelpers.ReadStyles(await DocxTestHelpers.RenderMarkdownAsync("```\ncode();\n```", profile: profile)),
            "CodeBlock");

        Assert.Equal("Fira Code", style.Descendants(W + "rFonts").First().Attribute(W + "ascii")!.Value);
        Assert.Equal("EEEEEE", style.Descendants(W + "shd").Single().Attribute(W + "fill")!.Value);
        Assert.Empty(style.Descendants(W + "pBdr"));
        // Кегль без поправки = основной, 14 пт → 28 в половинах пункта.
        Assert.Equal("28", style.Descendants(W + "sz").First().Attribute(W + "val")!.Value);
    }

    [Fact]
    public async Task Code_block_style_falls_back_when_the_profile_has_no_rule()
    {
        var profile = DocxTestHelpers.DefaultProfile with { CodeBlock = null };

        var style = FindStyle(
            DocxTestHelpers.ReadStyles(await DocxTestHelpers.RenderMarkdownAsync("```\ncode();\n```", profile: profile)),
            "CodeBlock");

        Assert.Equal("Consolas", style.Descendants(W + "rFonts").First().Attribute(W + "ascii")!.Value);
        Assert.NotEmpty(style.Descendants(W + "pBdr"));
        Assert.Empty(style.Descendants(W + "shd"));
    }

    [Fact]
    public async Task Missing_image_leaves_a_visible_placeholder_instead_of_disappearing()
    {
        var model = new ReportDocumentModel(
            null,
            [new ImageBlock("нет-такого-файла.png", "Схема")],
            GenerateTableOfContents: false);

        var docx = await DocxTestHelpers.RenderAsync(model);

        Assert.Empty(DocxTestHelpers.ReadDocument(docx).Descendants(W + "drawing"));
        Assert.Contains(
            DocxTestHelpers.ReadParagraphs(docx),
            text => text.Contains("Изображение не найдено", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Code_block_uses_its_own_style()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("```csharp\nvar x = 1;\n```");

        Assert.Contains(
            "CodeBlock",
            DocxTestHelpers.ReadDocument(docx)
                .Descendants(W + "pStyle")
                .Select(element => element.Attribute(W + "val")!.Value));

        var style = FindStyle(DocxTestHelpers.ReadStyles(docx), "CodeBlock");
        Assert.Equal("Consolas", style.Descendants(W + "rFonts").First().Attribute(W + "ascii")!.Value);
    }

    [Fact]
    public async Task Table_of_contents_is_a_field_over_the_profile_levels()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел", tableOfContents: true);
        var field = DocxTestHelpers.ReadDocument(docx).Descendants(W + "fldSimple").Single();

        Assert.Contains("TOC", field.Attribute(W + "instr")!.Value, StringComparison.Ordinal);
        Assert.Contains("\"1-3\"", field.Attribute(W + "instr")!.Value, StringComparison.Ordinal);
        Assert.Equal("true", field.Attribute(W + "dirty")!.Value);
        Assert.Contains("СОДЕРЖАНИЕ", DocxTestHelpers.ReadParagraphs(docx));
    }

    [Fact]
    public async Task Without_the_flag_there_is_no_table_of_contents()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел", tableOfContents: false);

        Assert.Empty(DocxTestHelpers.ReadDocument(docx).Descendants(W + "fldSimple"));
        Assert.DoesNotContain("СОДЕРЖАНИЕ", DocxTestHelpers.ReadParagraphs(docx));
    }

    [Fact]
    public async Task Word_is_asked_to_refresh_fields_on_open()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел", tableOfContents: true);
        var settings = DocxTestHelpers.ReadSettings(docx);

        Assert.Equal("true", settings.Descendants(W + "updateFields").Single().Attribute(W + "val")!.Value);
    }

    [Fact]
    public async Task Title_page_carries_every_supplied_field()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел", titlePage: SampleTitlePage);
        var text = DocxTestHelpers.ReadText(docx);

        Assert.Contains("Технологический университет", text, StringComparison.Ordinal);
        Assert.Contains("Кафедра Программной инженерии", text, StringComparison.Ordinal);
        Assert.Contains("Отчёт по лабораторной работе", text, StringComparison.Ordinal);
        Assert.Contains("по дисциплине «Математический анализ»", text, StringComparison.Ordinal);
        Assert.Contains("студент группы ИВТ-201", text, StringComparison.Ordinal);
        Assert.Contains("Иванов И. И.", text, StringComparison.Ordinal);
        Assert.Contains("Петров П. П.", text, StringComparison.Ordinal);
        Assert.Contains("Новосибирск 2026", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_number_is_printed_in_a_footer_and_the_title_page_stays_clean()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел", titlePage: SampleTitlePage);
        var section = DocxTestHelpers.ReadDocument(docx).Descendants(W + "sectPr").Single();
        var footers = DocxTestHelpers.ReadFooters(docx);

        Assert.NotNull(section.Element(W + "titlePg"));
        Assert.Equal(2, section.Elements(W + "footerReference").Count());
        Assert.Equal(2, footers.Count);

        // Один колонтитул с полем PAGE, второй — пустой, для титульного листа.
        Assert.Single(footers, footer => footer.Descendants(W + "instrText").Any(
            instruction => instruction.Value.Contains("PAGE", StringComparison.Ordinal)));
        Assert.Single(footers, footer => !footer.Descendants(W + "instrText").Any());
    }

    [Fact]
    public async Task Without_a_title_page_the_first_page_keeps_its_number()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("# Раздел");
        var section = DocxTestHelpers.ReadDocument(docx).Descendants(W + "sectPr").Single();

        Assert.Null(section.Element(W + "titlePg"));
        Assert.Single(section.Elements(W + "footerReference"));
    }

    [Fact]
    public async Task Hyperlink_becomes_a_real_relationship()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("см. [сайт](https://example.com)");
        var link = DocxTestHelpers.ReadDocument(docx).Descendants(W + "hyperlink").Single();

        Assert.NotNull(link.Attribute(XName.Get(
            "id",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships")));
        Assert.Empty(DocxTestHelpers.Validate(docx));
    }

    [Fact]
    public async Task Empty_document_still_renders_a_valid_package()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync(string.Empty);

        Assert.Empty(DocxTestHelpers.Validate(docx));
    }

    [Fact]
    public async Task Hard_break_becomes_a_line_break_element()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("первая  \nвторая");
        var document = DocxTestHelpers.ReadDocument(docx);

        // Разрывы страниц титульного листа тоже w:br, поэтому считаем только разрывы строк.
        Assert.Contains(
            document.Descendants(W + "br"),
            element => element.Attribute(W + "type") is null);
        Assert.Empty(DocxTestHelpers.Validate(docx));
    }

    [Fact]
    public async Task Soft_break_stays_a_space_and_does_not_split_the_paragraph()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("первая\nвторая");
        var document = DocxTestHelpers.ReadDocument(docx);

        // Мягкий перенос в отчёте по ГОСТ — пробел (CommonMark): текст абзаца остаётся сплошным,
        // и лишних w:r от него не появляется. Переносится он только в предпросмотре заметки.
        Assert.DoesNotContain(
            document.Descendants(W + "br"),
            element => element.Attribute(W + "type") is null);
        Assert.Contains(
            document.Descendants(W + "t"),
            element => element.Value == "первая вторая");
    }

    [Fact]
    public async Task Formula_reaches_the_document_as_a_monospace_source()
    {
        var docx = await DocxTestHelpers.RenderMarkdownAsync("формула $E = mc^2$ здесь");
        var document = DocxTestHelpers.ReadDocument(docx);

        var run = Assert.Single(
            document.Descendants(W + "r"),
            element => element.Element(W + "t")?.Value == "E = mc^2");

        var properties = run.Element(W + "rPr")!;
        Assert.Equal("Consolas", properties.Element(W + "rFonts")!.Attribute(W + "ascii")!.Value);
        Assert.NotNull(properties.Element(W + "i"));
        Assert.Empty(DocxTestHelpers.Validate(docx));
    }

    private static XElement FindStyle(XDocument styles, string styleId) =>
        styles.Descendants(W + "style").Single(style => style.Attribute(W + "styleId")?.Value == styleId);

    /// <summary>Подменяет путь картинки из фикстуры на реальный data-URI, чтобы тест не зависел от файлов.</summary>
    private static ReportDocumentModel WithInlineImage(ReportDocumentModel model) => model with
    {
        Blocks =
        [
            .. model.Blocks.Select(block => block is ImageBlock image
                ? image with { PathOrBase64 = TestImages.SampleDataUri }
                : block),
        ],
    };
}
