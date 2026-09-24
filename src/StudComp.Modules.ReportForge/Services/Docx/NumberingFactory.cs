using System.Globalization;
using DocumentFormat.OpenXml.Wordprocessing;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Собирает <c>numbering.xml</c>: одна нумерация для маркированных списков (маркер берётся из профиля,
/// ADR §16.32) и одна для нумерованных.
/// </summary>
internal static class NumberingFactory
{
    /// <summary>Столько уровней вложенности списков поддерживается; более глубокие прижимаются к последнему.</summary>
    internal const int LevelCount = 3;

    /// <summary><c>w:numId</c> маркированного списка.</summary>
    internal const int BulletNumberId = 1;

    /// <summary><c>w:numId</c> нумерованного списка.</summary>
    internal const int OrderedNumberId = 2;

    private const int BulletAbstractId = 0;
    private const int OrderedAbstractId = 1;

    /// <summary>Отступ пункта первого уровня и шаг вложенности в twip (стандартные полдюйма Word).</summary>
    private const int LevelIndentTwips = 720;

    private const int HangingIndentTwips = 360;

    internal static Numbering Create(GostStyleProfile profile)
    {
        var marker = string.IsNullOrEmpty(profile.BulletMarker) ? "–" : profile.BulletMarker;

        return new Numbering(
            BuildAbstract(BulletAbstractId, ordered: false, marker, profile.FontFamily),
            BuildAbstract(OrderedAbstractId, ordered: true, marker, profile.FontFamily),
            new NumberingInstance(new AbstractNumId { Val = BulletAbstractId }) { NumberID = BulletNumberId },
            new NumberingInstance(new AbstractNumId { Val = OrderedAbstractId }) { NumberID = OrderedNumberId });
    }

    /// <summary><c>w:numId</c> для списка нужного вида.</summary>
    internal static int NumberIdFor(bool ordered) => ordered ? OrderedNumberId : BulletNumberId;

    private static AbstractNum BuildAbstract(int abstractId, bool ordered, string marker, string fontFamily)
    {
        var abstractNum = new AbstractNum(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        {
            AbstractNumberId = abstractId,
        };

        for (var level = 0; level < LevelCount; level++)
        {
            abstractNum.Append(BuildLevel(level, ordered, marker, fontFamily));
        }

        return abstractNum;
    }

    private static Level BuildLevel(int level, bool ordered, string marker, string fontFamily)
    {
        // Порядок детей w:lvl задан схемой: start → numFmt → lvlText → lvlJc → pPr → rPr.
        var element = new Level
        {
            LevelIndex = level,
            StartNumberingValue = new StartNumberingValue { Val = 1 },
            NumberingFormat = new NumberingFormat
            {
                Val = ordered ? NumberFormatValues.Decimal : NumberFormatValues.Bullet,
            },
            LevelText = new LevelText
            {
                Val = ordered ? $"%{(level + 1).ToString(CultureInfo.InvariantCulture)}." : marker,
            },
            LevelJustification = new LevelJustification { Val = LevelJustificationValues.Left },
        };

        element.Append(new PreviousParagraphProperties(new Indentation
        {
            Left = DocxUnits.Twips(LevelIndentTwips * (level + 1)),
            Hanging = DocxUnits.Twips(HangingIndentTwips),
        }));

        // Маркер печатается тем же шрифтом, что и текст, — иначе Word подставит Symbol и вместо тире выйдет кружок.
        element.Append(new NumberingSymbolRunProperties(new RunFonts
        {
            Ascii = fontFamily,
            HighAnsi = fontFamily,
            ComplexScript = fontFamily,
            Hint = FontTypeHintValues.Default,
        }));

        return element;
    }
}
