using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using StudComp.Modules.ReportForge.Services;

namespace StudComp.ViewModels.ReportForge;

/// <summary>
/// Профиль «Шпаргалка» для макета <see cref="CardComposeLayout.CheatSheet"/> (new_addons.md §7.5).
/// </summary>
/// <remarks>
/// Заводится лениво при первом использовании через уже готовый <see cref="IReportTemplateService.CloneAsync"/>
/// — ровно так же, как <c>EnsureDefaultTemplateAsync</c> заводит заводской ГОСТ-профиль по требованию,
/// а не сидом-миграцией: профиль не нужен, пока пользователь ни разу не собрал шпаргалку.
/// </remarks>
internal static class CheatSheetTemplateProvider
{
    public const string Name = "Шпаргалка";

    public static async Task<ReportTemplate> EnsureAsync(IReportTemplateService templates, CancellationToken ct = default)
    {
        var existing = (await templates.GetAllAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(t => t.Name == Name);
        if (existing is not null)
        {
            return existing;
        }

        var basedOn = await templates.EnsureDefaultTemplateAsync(ct).ConfigureAwait(false);
        var clone = await templates.CloneAsync(basedOn.Id, Name, ct).ConfigureAwait(false);
        var profile = await templates.GetProfileAsync(clone.Id, ct).ConfigureAwait(false);

        var compact = profile with
        {
            FontSizePt = 10,
            LineSpacing = 1.0,
            ParagraphIndentCm = 0,
            Margins = new MarginsMm(10, 10, 10, 10),
        };

        await templates.SaveAsync(clone.Id, Name, compact, ct).ConfigureAwait(false);
        return clone;
    }
}
