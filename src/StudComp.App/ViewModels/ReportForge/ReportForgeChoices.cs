using StudComp.Core.Abstractions.ReportForge;
using StudComp.ViewModels;

namespace StudComp.ViewModels.ReportForge;

/// <summary>Готовые списки вариантов для выпадающих списков формы профиля оформления.</summary>
public static class ReportForgeChoices
{
    public static IReadOnlyList<NamedChoice<ParagraphAlignment>> Alignments { get; } =
    [
        new(ParagraphAlignment.Left, "По левому краю"),
        new(ParagraphAlignment.Center, "По центру"),
        new(ParagraphAlignment.Right, "По правому краю"),
        new(ParagraphAlignment.Justify, "По ширине"),
    ];

    public static IReadOnlyList<NamedChoice<PageNumberPosition>> PageNumberPositions { get; } =
    [
        new(PageNumberPosition.BottomCenter, "Внизу по центру"),
        new(PageNumberPosition.BottomRight, "Внизу справа"),
        new(PageNumberPosition.TopCenter, "Вверху по центру"),
        new(PageNumberPosition.TopRight, "Вверху справа"),
    ];

    public static string AlignmentName(ParagraphAlignment value) =>
        Alignments.First(a => a.Value == value).Display;
}
