using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Параметры секции: лист A4, поля из профиля и колонтитул с номером страницы (ARCHITECTURE §10.4).
/// </summary>
internal static class SectionFactory
{
    /// <summary>
    /// Собирает <c>w:sectPr</c> и, если нумерация включена, заводит нужные части колонтитулов.
    /// </summary>
    /// <param name="main">Часть документа — в неё добавляются части колонтитулов.</param>
    /// <param name="profile">Профиль оформления.</param>
    /// <param name="hasTitlePage">
    /// Есть ли титульный лист. Пропуск номера на первой странице имеет смысл только с ним, иначе
    /// пользователь потеряет номер на обычной первой странице.
    /// </param>
    internal static SectionProperties Create(MainDocumentPart main, GostStyleProfile profile, bool hasTitlePage)
    {
        var section = new SectionProperties();
        var numbering = profile.PageNumbering;
        var skipFirst = numbering.SkipTitlePage && hasTitlePage;

        if (numbering.Enabled)
        {
            var atTop = numbering.Position is PageNumberPosition.TopCenter or PageNumberPosition.TopRight;
            var alignment = numbering.Position is PageNumberPosition.BottomRight or PageNumberPosition.TopRight
                ? JustificationValues.Right
                : JustificationValues.Center;

            if (atTop)
            {
                section.Append(BuildHeaderReference(main, profile, alignment, HeaderFooterValues.Default));

                if (skipFirst)
                {
                    section.Append(BuildEmptyHeaderReference(main));
                }
            }
            else
            {
                section.Append(BuildFooterReference(main, profile, alignment, HeaderFooterValues.Default));

                if (skipFirst)
                {
                    section.Append(BuildEmptyFooterReference(main));
                }
            }
        }

        section.Append(new PageSize
        {
            Width = (uint)DocxUnits.A4WidthTwips,
            Height = (uint)DocxUnits.A4HeightTwips,
            Orient = PageOrientationValues.Portrait,
        });

        section.Append(new PageMargin
        {
            Left = (uint)DocxUnits.MmToTwips(profile.Margins.Left),
            Right = (uint)DocxUnits.MmToTwips(profile.Margins.Right),
            Top = DocxUnits.MmToTwips(profile.Margins.Top),
            Bottom = DocxUnits.MmToTwips(profile.Margins.Bottom),
            Header = (uint)DocxUnits.MmToTwips(12.5),
            Footer = (uint)DocxUnits.MmToTwips(12.5),
            Gutter = 0,
        });

        if (skipFirst)
        {
            // «Первая страница особая»: её колонтитул пустой, поэтому титул остаётся без номера,
            // но в общем счёте страниц участвует (ARCHITECTURE §10.4).
            section.Append(new TitlePage());
        }

        return section;
    }

    /// <summary>Полезная ширина строки — лист минус поля. Нужна рендереру таблиц и картинок.</summary>
    internal static int TextWidthTwips(GostStyleProfile profile) =>
        DocxUnits.A4WidthTwips
        - DocxUnits.MmToTwips(profile.Margins.Left)
        - DocxUnits.MmToTwips(profile.Margins.Right);

    /// <summary>Полезная высота полосы набора — лист минус верхнее и нижнее поля. Ограничивает высоту картинок.</summary>
    internal static int TextHeightTwips(GostStyleProfile profile) =>
        DocxUnits.A4HeightTwips
        - DocxUnits.MmToTwips(profile.Margins.Top)
        - DocxUnits.MmToTwips(profile.Margins.Bottom);

    private static FooterReference BuildFooterReference(
        MainDocumentPart main,
        GostStyleProfile profile,
        EnumValue<JustificationValues> alignment,
        HeaderFooterValues type)
    {
        var part = main.AddNewPart<FooterPart>();
        part.Footer = new Footer(BuildPageNumberParagraph(profile, alignment));
        return new FooterReference { Id = main.GetIdOfPart(part), Type = type };
    }

    private static FooterReference BuildEmptyFooterReference(MainDocumentPart main)
    {
        var part = main.AddNewPart<FooterPart>();
        part.Footer = new Footer(new Paragraph());
        return new FooterReference { Id = main.GetIdOfPart(part), Type = HeaderFooterValues.First };
    }

    private static HeaderReference BuildHeaderReference(
        MainDocumentPart main,
        GostStyleProfile profile,
        EnumValue<JustificationValues> alignment,
        HeaderFooterValues type)
    {
        var part = main.AddNewPart<HeaderPart>();
        part.Header = new Header(BuildPageNumberParagraph(profile, alignment));
        return new HeaderReference { Id = main.GetIdOfPart(part), Type = type };
    }

    private static HeaderReference BuildEmptyHeaderReference(MainDocumentPart main)
    {
        var part = main.AddNewPart<HeaderPart>();
        part.Header = new Header(new Paragraph());
        return new HeaderReference { Id = main.GetIdOfPart(part), Type = HeaderFooterValues.First };
    }

    /// <summary>Абзац с полем <c>PAGE</c> — номер страницы Word подставляет сам при отрисовке.</summary>
    private static Paragraph BuildPageNumberParagraph(GostStyleProfile profile, EnumValue<JustificationValues> alignment)
    {
        var runProperties = new RunProperties(
            new RunFonts { Ascii = profile.FontFamily, HighAnsi = profile.FontFamily, ComplexScript = profile.FontFamily },
            new FontSize { Val = DocxUnits.FontSizeHalfPoints(profile.FontSizePt) });

        return new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { Line = "240", LineRule = LineSpacingRuleValues.Auto, Before = "0", After = "0" },
                new Indentation { FirstLine = "0" },
                new Justification { Val = alignment }),
            new Run(runProperties.CloneNode(true), new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(
                runProperties.CloneNode(true),
                new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(runProperties.CloneNode(true), new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(runProperties.CloneNode(true), new Text("1")),
            new Run(runProperties, new FieldChar { FieldCharType = FieldCharValues.End }));
    }
}
