using System.Globalization;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Перевод человеческих единиц профиля (мм, см, пункты) в единицы OOXML. WordprocessingML меряет
/// почти всё в twip (1/20 пункта), кегли — в половинах пункта, а графику — в EMU (1/914400 дюйма).
/// </summary>
internal static class DocxUnits
{
    /// <summary>Ширина листа A4 в twip (210 мм).</summary>
    internal const int A4WidthTwips = 11906;

    /// <summary>Высота листа A4 в twip (297 мм).</summary>
    internal const int A4HeightTwips = 16838;

    /// <summary>EMU в дюйме — константа OOXML.</summary>
    internal const long EmuPerInch = 914400;

    private const double TwipsPerMillimetre = 1440d / 25.4d;

    internal static int MmToTwips(double millimetres) =>
        (int)Math.Round(millimetres * TwipsPerMillimetre, MidpointRounding.AwayFromZero);

    internal static int CmToTwips(double centimetres) => MmToTwips(centimetres * 10d);

    /// <summary>Кегль в half-points — так его хранит <c>w:sz</c>.</summary>
    internal static string FontSizeHalfPoints(double pointSize) =>
        ((int)Math.Round(pointSize * 2d, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Межстрочный интервал в единицах <c>w:line</c> при <c>w:lineRule="auto"</c>: одинарный — 240.
    /// </summary>
    internal static string LineSpacing(double multiplier) =>
        ((int)Math.Round(multiplier * 240d, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    internal static long PixelsToEmu(double pixels, double dpi) =>
        (long)Math.Round(pixels / dpi * EmuPerInch, MidpointRounding.AwayFromZero);

    internal static long TwipsToEmu(int twips) =>
        (long)Math.Round(twips / 1440d * EmuPerInch, MidpointRounding.AwayFromZero);

    internal static string Twips(int twips) => twips.ToString(CultureInfo.InvariantCulture);
}
