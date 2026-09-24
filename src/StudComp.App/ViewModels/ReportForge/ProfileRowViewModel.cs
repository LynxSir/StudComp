using System.Globalization;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.ReportForge;

/// <summary>Строка списка «Профили оформления»: шаблон + краткая сводка его профиля.</summary>
public sealed class ProfileRowViewModel(ReportTemplate template, GostStyleProfile profile)
{
    public ReportTemplate Template { get; } = template;

    public Guid Id => Template.Id;

    public string Name => Template.Name;

    /// <summary>Заводской профиль ГОСТ — его нельзя удалить и не предлагаем переименовывать.</summary>
    public bool IsFactoryDefault => Template.GostVariant == "7.32-2017";

    public string SummaryLine =>
        $"{profile.FontFamily}, {Format(profile.FontSizePt)} пт · "
        + $"поля {Format(profile.Margins.Left)}/{Format(profile.Margins.Right)}/"
        + $"{Format(profile.Margins.Top)}/{Format(profile.Margins.Bottom)} мм · "
        + $"интервал {Format(profile.LineSpacing)}";

    private static string Format(double value) =>
        value.ToString(value % 1d == 0d ? "0" : "0.##", CultureInfo.InvariantCulture);
}
