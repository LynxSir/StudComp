using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Оглавление полем <c>{ TOC }</c>, а не расчётом номеров страниц: пагинация — дело Word, она зависит
/// от шрифтов и драйвера печати, повторять её в генераторе ненадёжно (ADR §16.6).
/// </summary>
internal static class TocWriter
{
    /// <summary>Заголовок раздела оглавления. Намеренно без стиля «heading» — иначе попадёт сам в себя.</summary>
    private const string Title = "СОДЕРЖАНИЕ";

    private const string Placeholder = "Оглавление собирается при обновлении полей (Ctrl+A, затем F9).";

    internal static IEnumerable<Paragraph> Build(GostStyleProfile profile)
    {
        yield return ParagraphFactory.Text(profile, Title, JustificationValues.Center, bold: true);
        yield return ParagraphFactory.Spacer(profile);
        yield return BuildField(profile);
        yield return ParagraphFactory.PageBreak();
    }

    /// <summary>
    /// Инструкция поля: уровни из профиля, <c>\h</c> — гиперссылки на разделы, <c>\z</c> — скрыть
    /// табуляцию в веб-виде, <c>\u</c> — брать уровни из структуры документа.
    /// </summary>
    internal static string BuildInstruction(TocOptions options)
    {
        var min = Math.Max(1, options.MinLevel);
        var max = Math.Max(min, options.MaxLevel);
        return string.Create(
            CultureInfo.InvariantCulture,
            $" TOC \\o \"{min}-{max}\" \\h \\z \\u ");
    }

    private static Paragraph BuildField(GostStyleProfile profile)
    {
        var properties = new ParagraphProperties(
            new SpacingBetweenLines
            {
                Line = DocxUnits.LineSpacing(profile.LineSpacing),
                LineRule = LineSpacingRuleValues.Auto,
            },
            new Indentation { FirstLine = "0" },
            new Justification { Val = JustificationValues.Left });

        var placeholder = new Run(
            new RunProperties(new RunFonts
            {
                Ascii = profile.FontFamily,
                HighAnsi = profile.FontFamily,
                ComplexScript = profile.FontFamily,
            }),
            new Text(Placeholder) { Space = SpaceProcessingModeValues.Preserve });

        // Dirty=true — Word при открытии предложит обновить поле и соберёт оглавление сам.
        var field = new SimpleField(placeholder)
        {
            Instruction = BuildInstruction(profile.TableOfContents),
            Dirty = true,
        };

        return new Paragraph(properties, field);
    }
}
