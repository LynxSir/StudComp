using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Собирает <c>styles.xml</c> из профиля: базовый стиль текста, стили заголовков, подписи,
/// листинги, таблицы и ссылки (ARCHITECTURE §10.4).
/// </summary>
internal static class StyleDefinitionsFactory
{
    /// <summary>Идентификатор стиля подписей «Таблица N — …» / «Рисунок N — …».</summary>
    internal const string CaptionStyleId = "Caption";

    /// <summary>Идентификатор стиля листинга кода (ARCHITECTURE §10.5).</summary>
    internal const string CodeBlockStyleId = "CodeBlock";

    internal const string TableStyleId = "TableGrid";

    internal const string HyperlinkStyleId = "Hyperlink";

    internal const string NormalStyleId = "Normal";

    /// <summary>Оформление листинга, когда в профиле нет секции <c>codeBlock</c> (старый JSON из БД).</summary>
    internal static readonly CodeBlockStyleRule DefaultCodeBlock = new("Consolas", -2d, Boxed: true, BackgroundHex: null);

    /// <summary>Идентификатор стиля заголовка уровня <paramref name="level"/>: <c>Heading1</c>, <c>Heading2</c>, …</summary>
    internal static string HeadingStyleId(int level) => $"Heading{level}";

    internal static Styles Create(GostStyleProfile profile)
    {
        var styles = new Styles(BuildDocDefaults(profile));

        styles.Append(BuildNormal(profile));

        foreach (var rule in profile.HeadingRules.OrderBy(r => r.Level))
        {
            styles.Append(BuildHeading(profile, rule));
        }

        styles.Append(BuildCaption(profile));
        styles.Append(BuildCodeBlock(profile));
        styles.Append(BuildTableStyle());
        styles.Append(BuildHyperlink());

        return styles;
    }

    private static DocDefaults BuildDocDefaults(GostStyleProfile profile) => new(
        new RunPropertiesDefault(new RunPropertiesBaseStyle(
            BuildRunFonts(profile.FontFamily),
            new FontSize { Val = DocxUnits.FontSizeHalfPoints(profile.FontSizePt) },
            new FontSizeComplexScript { Val = DocxUnits.FontSizeHalfPoints(profile.FontSizePt) })),
        new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
            BuildBodySpacing(profile),
            new Indentation { FirstLine = DocxUnits.Twips(DocxUnits.CmToTwips(profile.ParagraphIndentCm)) },
            new Justification { Val = ToJustification(profile.BodyAlignment) })));

    private static Style BuildNormal(GostStyleProfile profile) => new()
    {
        Type = StyleValues.Paragraph,
        StyleId = NormalStyleId,
        Default = true,
        StyleName = new StyleName { Val = "Normal" },
        PrimaryStyle = new PrimaryStyle(),
        StyleParagraphProperties = new StyleParagraphProperties(
            BuildBodySpacing(profile),
            new Indentation { FirstLine = DocxUnits.Twips(DocxUnits.CmToTwips(profile.ParagraphIndentCm)) },
            new Justification { Val = ToJustification(profile.BodyAlignment) }),
        StyleRunProperties = new StyleRunProperties(
            BuildRunFonts(profile.FontFamily),
            new FontSize { Val = DocxUnits.FontSizeHalfPoints(profile.FontSizePt) },
            new FontSizeComplexScript { Val = DocxUnits.FontSizeHalfPoints(profile.FontSizePt) }),
    };

    private static Style BuildHeading(GostStyleProfile profile, HeadingStyleRule rule)
    {
        // Порядок детей w:pPr задан схемой: keepNext → pageBreakBefore → spacing → ind → jc → outlineLvl.
        var paragraphProperties = new StyleParagraphProperties(new KeepNext());

        if (rule.PageBreakBefore)
        {
            paragraphProperties.Append(new PageBreakBefore());
        }

        paragraphProperties.Append(BuildBodySpacing(profile));
        paragraphProperties.Append(new Indentation { FirstLine = "0" });
        paragraphProperties.Append(new Justification { Val = ToJustification(rule.Alignment) });

        // Без outlineLvl поле { TOC \o } не увидит заголовок — это и есть связка стиля с оглавлением.
        paragraphProperties.Append(new OutlineLevel { Val = Math.Clamp(rule.Level - 1, 0, 8) });

        var runProperties = new StyleRunProperties(BuildRunFonts(profile.FontFamily));

        if (rule.Bold)
        {
            runProperties.Append(new Bold());
            runProperties.Append(new BoldComplexScript());
        }

        if (rule.UpperCase)
        {
            runProperties.Append(new Caps());
        }

        runProperties.Append(new FontSize { Val = DocxUnits.FontSizeHalfPoints(rule.FontSizePt) });
        runProperties.Append(new FontSizeComplexScript { Val = DocxUnits.FontSizeHalfPoints(rule.FontSizePt) });

        return new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = HeadingStyleId(rule.Level),
            // Word узнаёт заголовок по имени «heading N» — оно должно быть именно таким.
            StyleName = new StyleName { Val = $"heading {rule.Level}" },
            BasedOn = new BasedOn { Val = NormalStyleId },
            NextParagraphStyle = new NextParagraphStyle { Val = NormalStyleId },
            PrimaryStyle = new PrimaryStyle(),
            StyleParagraphProperties = paragraphProperties,
            StyleRunProperties = runProperties,
        };
    }

    private static Style BuildCaption(GostStyleProfile profile) => new()
    {
        Type = StyleValues.Paragraph,
        StyleId = CaptionStyleId,
        StyleName = new StyleName { Val = "caption" },
        BasedOn = new BasedOn { Val = NormalStyleId },
        NextParagraphStyle = new NextParagraphStyle { Val = NormalStyleId },
        PrimaryStyle = new PrimaryStyle(),
        StyleParagraphProperties = new StyleParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines
            {
                Line = DocxUnits.LineSpacing(profile.LineSpacing),
                LineRule = LineSpacingRuleValues.Auto,
                Before = "120",
                After = "120",
            },
            new Indentation { FirstLine = "0" },
            new Justification { Val = JustificationValues.Left }),
        StyleRunProperties = new StyleRunProperties(BuildRunFonts(profile.FontFamily)),
    };

    private static Style BuildCodeBlock(GostStyleProfile profile)
    {
        var rule = profile.CodeBlock ?? DefaultCodeBlock;
        var fontFamily = string.IsNullOrWhiteSpace(rule.MonospaceFontFamily) ? "Consolas" : rule.MonospaceFontFamily;
        var sizePt = Math.Max(8d, profile.FontSizePt + rule.RelativeFontSizePt);

        // Порядок детей w:pPr задан схемой: keepLines → pBdr → shd → spacing → ind → jc.
        var paragraphProperties = new StyleParagraphProperties(new KeepLines());

        if (rule.Boxed)
        {
            paragraphProperties.Append(new ParagraphBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Space = 4, Color = "BFBFBF" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Space = 4, Color = "BFBFBF" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Space = 4, Color = "BFBFBF" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Space = 4, Color = "BFBFBF" }));
        }

        if (NormalizeHex(rule.BackgroundHex) is { } fill)
        {
            paragraphProperties.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill });
        }

        // Листинг набирается без ГОСТ-типографики абзаца: одинарный интервал, без красной строки.
        paragraphProperties.Append(new SpacingBetweenLines
        {
            Line = "240",
            LineRule = LineSpacingRuleValues.Auto,
            Before = "60",
            After = "60",
        });
        paragraphProperties.Append(new Indentation { FirstLine = "0", Left = "142" });
        paragraphProperties.Append(new Justification { Val = JustificationValues.Left });

        return new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = CodeBlockStyleId,
            StyleName = new StyleName { Val = "Code Block" },
            BasedOn = new BasedOn { Val = NormalStyleId },
            NextParagraphStyle = new NextParagraphStyle { Val = NormalStyleId },
            PrimaryStyle = new PrimaryStyle(),
            StyleParagraphProperties = paragraphProperties,
            StyleRunProperties = new StyleRunProperties(
                BuildRunFonts(fontFamily),
                new FontSize { Val = DocxUnits.FontSizeHalfPoints(sizePt) },
                new FontSizeComplexScript { Val = DocxUnits.FontSizeHalfPoints(sizePt) }),
        };
    }

    /// <summary>Приводит цвет подложки к виду <c>RRGGBB</c> (Word ждёт hex без решётки); мусор → без заливки.</summary>
    private static string? NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var trimmed = hex.Trim().TrimStart('#');
        return trimmed.Length == 6 && trimmed.All(Uri.IsHexDigit)
            ? trimmed.ToUpperInvariant()
            : null;
    }

    private static Style BuildTableStyle() => new()
    {
        Type = StyleValues.Table,
        StyleId = TableStyleId,
        StyleName = new StyleName { Val = "Table Grid" },
        BasedOn = new BasedOn { Val = "TableNormal" },
        PrimaryStyle = new PrimaryStyle(),
        StyleTableProperties = new StyleTableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" })),
    };

    private static Style BuildHyperlink() => new()
    {
        Type = StyleValues.Character,
        StyleId = HyperlinkStyleId,
        StyleName = new StyleName { Val = "Hyperlink" },
        StyleRunProperties = new StyleRunProperties(
            new Color { Val = "0563C1" },
            new Underline { Val = UnderlineValues.Single }),
    };

    private static SpacingBetweenLines BuildBodySpacing(GostStyleProfile profile) => new()
    {
        Line = DocxUnits.LineSpacing(profile.LineSpacing),
        LineRule = LineSpacingRuleValues.Auto,
        Before = "0",
        After = "0",
    };

    private static RunFonts BuildRunFonts(string fontFamily) => new()
    {
        Ascii = fontFamily,
        HighAnsi = fontFamily,
        ComplexScript = fontFamily,
        EastAsia = fontFamily,
    };

    internal static EnumValue<JustificationValues> ToJustification(ParagraphAlignment alignment) => alignment switch
    {
        ParagraphAlignment.Center => JustificationValues.Center,
        ParagraphAlignment.Right => JustificationValues.Right,
        ParagraphAlignment.Justify => JustificationValues.Both,
        _ => JustificationValues.Left,
    };
}
