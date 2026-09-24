using DocumentFormat.OpenXml.Wordprocessing;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Титульный лист по данным <see cref="TitlePageInfo"/> (ARCHITECTURE §10.5): шапка вуза сверху,
/// тип работы и предмет в середине, исполнитель и руководитель справа, город и год снизу.
/// </summary>
/// <remarks>
/// Раскладка встроенная. <c>GostStyleProfile.TitlePageTemplate</c> в Phase 6 не читается — свой шаблон
/// титульника появится в Phase 9 вместе с редактором профилей.
/// </remarks>
internal static class TitlePageWriter
{
    /// <summary>Сколько пустых абзацев отделяет шапку от названия работы.</summary>
    private const int GapBeforeWorkType = 6;

    private const int GapBeforeAuthor = 6;

    private const int GapBeforeCity = 4;

    internal static IEnumerable<Paragraph> Build(TitlePageInfo info, GostStyleProfile profile)
    {
        yield return ParagraphFactory.Text(profile, info.University, JustificationValues.Center, bold: true, caps: true);

        if (HasText(info.Faculty))
        {
            yield return ParagraphFactory.Text(profile, info.Faculty!, JustificationValues.Center);
        }

        if (HasText(info.Department))
        {
            yield return ParagraphFactory.Text(profile, $"Кафедра {info.Department}", JustificationValues.Center);
        }

        foreach (var spacer in Spacers(profile, GapBeforeWorkType))
        {
            yield return spacer;
        }

        yield return ParagraphFactory.Text(
            profile,
            info.WorkType,
            JustificationValues.Center,
            bold: true,
            caps: true,
            fontSizePt: profile.FontSizePt + 2d);

        if (HasText(info.SubjectName))
        {
            yield return ParagraphFactory.Text(
                profile,
                $"по дисциплине «{info.SubjectName}»",
                JustificationValues.Center);
        }

        foreach (var spacer in Spacers(profile, GapBeforeAuthor))
        {
            yield return spacer;
        }

        yield return ParagraphFactory.Text(profile, "Выполнил:", JustificationValues.Right);
        yield return ParagraphFactory.Text(
            profile,
            HasText(info.StudentGroup) ? $"студент группы {info.StudentGroup}" : "студент",
            JustificationValues.Right);
        yield return ParagraphFactory.Text(profile, info.StudentName, JustificationValues.Right);

        if (HasText(info.SupervisorName))
        {
            yield return ParagraphFactory.Spacer(profile);
            yield return ParagraphFactory.Text(profile, "Проверил:", JustificationValues.Right);
            yield return ParagraphFactory.Text(profile, info.SupervisorName!, JustificationValues.Right);
        }

        foreach (var spacer in Spacers(profile, GapBeforeCity))
        {
            yield return spacer;
        }

        var footer = BuildCityAndYear(info);
        if (HasText(footer))
        {
            yield return ParagraphFactory.Text(profile, footer!, JustificationValues.Center);
        }

        // Титул всегда занимает отдельную страницу, даже если текста на нём немного.
        yield return ParagraphFactory.PageBreak();
    }

    private static string? BuildCityAndYear(TitlePageInfo info) => (info.City, info.Year) switch
    {
        ({ Length: > 0 } city, { } year) => $"{city} {year}",
        ({ Length: > 0 } city, null) => city,
        (_, { } year) => year.ToString(),
        _ => null,
    };

    private static IEnumerable<Paragraph> Spacers(GostStyleProfile profile, int count) =>
        Enumerable.Range(0, count).Select(_ => ParagraphFactory.Spacer(profile));

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}
