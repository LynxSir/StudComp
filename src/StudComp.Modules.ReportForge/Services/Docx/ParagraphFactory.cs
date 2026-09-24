using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Мелкие абзацы прямым форматированием — титульный лист, шапка оглавления, служебные вставки.
/// Основной текст идёт через стили из <see cref="StyleDefinitionsFactory"/>, здесь стилей нет
/// намеренно: эти абзацы не должны попадать в оглавление и не наследуют красную строку.
/// </summary>
internal static class ParagraphFactory
{
    /// <summary>Абзац простым текстом без абзацного отступа.</summary>
    internal static Paragraph Text(
        GostStyleProfile profile,
        string text,
        EnumValue<JustificationValues> alignment,
        bool bold = false,
        bool caps = false,
        double? fontSizePt = null,
        bool pageBreakBefore = false)
    {
        var properties = new ParagraphProperties();

        if (pageBreakBefore)
        {
            properties.Append(new PageBreakBefore());
        }

        properties.Append(new SpacingBetweenLines
        {
            Line = DocxUnits.LineSpacing(profile.LineSpacing),
            LineRule = LineSpacingRuleValues.Auto,
            Before = "0",
            After = "0",
        });
        properties.Append(new Indentation { FirstLine = "0" });
        properties.Append(new Justification { Val = alignment });

        return new Paragraph(properties, Run(profile, text, bold, caps, fontSizePt));
    }

    /// <summary>Пустой абзац — вертикальный воздух на титульном листе.</summary>
    internal static Paragraph Spacer(GostStyleProfile profile) =>
        Text(profile, string.Empty, JustificationValues.Left);

    /// <summary>Абзац, единственная задача которого — разорвать страницу.</summary>
    internal static Paragraph PageBreak() =>
        new(new Run(new Break { Type = BreakValues.Page }));

    private static Run Run(GostStyleProfile profile, string text, bool bold, bool caps, double? fontSizePt)
    {
        var size = DocxUnits.FontSizeHalfPoints(fontSizePt ?? profile.FontSizePt);
        var properties = new RunProperties(new RunFonts
        {
            Ascii = profile.FontFamily,
            HighAnsi = profile.FontFamily,
            ComplexScript = profile.FontFamily,
        });

        if (bold)
        {
            properties.Append(new Bold());
            properties.Append(new BoldComplexScript());
        }

        if (caps)
        {
            properties.Append(new Caps());
        }

        properties.Append(new FontSize { Val = size });
        properties.Append(new FontSizeComplexScript { Val = size });

        return new Run(properties, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
}
