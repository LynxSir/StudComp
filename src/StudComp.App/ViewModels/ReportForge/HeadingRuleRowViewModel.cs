using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.ViewModels;

namespace StudComp.ViewModels.ReportForge;

/// <summary>Строка редактора: оформление одного уровня заголовков (<see cref="HeadingStyleRule"/>).</summary>
public sealed partial class HeadingRuleRowViewModel : ObservableObject
{
    /// <summary>Дёргается на любое изменение поля — родитель перерисовывает предпросмотр.</summary>
    private readonly Action? _changed;

    public HeadingRuleRowViewModel(HeadingStyleRule rule, Action? changed = null)
    {
        _changed = changed;
        Level = rule.Level;
        _fontSizePt = rule.FontSizePt;
        _bold = rule.Bold;
        _pageBreakBefore = rule.PageBreakBefore;
        _upperCase = rule.UpperCase;
        _selectedAlignment = ReportForgeChoices.Alignments.FirstOrDefault(a => a.Value == rule.Alignment)
                             ?? ReportForgeChoices.Alignments[0];
    }

    /// <summary>Уровень заголовка (1..N). Не редактируется — задаётся позицией в списке.</summary>
    public int Level { get; }

    public string Title => $"Заголовок уровня {Level}";

    [ObservableProperty]
    private double _fontSizePt;

    [ObservableProperty]
    private bool _bold;

    [ObservableProperty]
    private bool _pageBreakBefore;

    [ObservableProperty]
    private bool _upperCase;

    [ObservableProperty]
    private NamedChoice<ParagraphAlignment> _selectedAlignment;

    public IReadOnlyList<NamedChoice<ParagraphAlignment>> Alignments => ReportForgeChoices.Alignments;

    public HeadingStyleRule ToRule() => new(
        Level,
        FontSizePt,
        Bold,
        PageBreakBefore,
        SelectedAlignment.Value,
        UpperCase);

    partial void OnFontSizePtChanged(double value) => _changed?.Invoke();

    partial void OnBoldChanged(bool value) => _changed?.Invoke();

    partial void OnPageBreakBeforeChanged(bool value) => _changed?.Invoke();

    partial void OnUpperCaseChanged(bool value) => _changed?.Invoke();

    partial void OnSelectedAlignmentChanged(NamedChoice<ParagraphAlignment> value) => _changed?.Invoke();
}
