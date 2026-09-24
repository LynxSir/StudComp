using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using StudComp.ViewModels;

namespace StudComp.ViewModels.ReportForge;

/// <summary>
/// Форма редактирования профиля оформления <see cref="GostStyleProfile"/> (ARCHITECTURE §10.4, Phase 9):
/// шрифт, поля, интервал, отступ, заголовки по уровням, нумерация, оглавление, листинги. Рядом —
/// живой предпросмотр макета страницы (см. <c>ProfilePreview</c>). Собирается вручную в
/// <see cref="ProfilesViewModel"/>, в DI не регистрируется.
/// </summary>
public sealed partial class ProfileEditorViewModel : ObservableObject
{
    /// <summary>Верхний предел уровней заголовков в форме — глубже вложенность в отчётах не встречается.</summary>
    private const int MaxHeadingLevels = 6;

    /// <summary>Масштаб предпросмотра: пикселей на миллиметр листа.</summary>
    private const double PreviewScale = 1.24d;

    /// <summary>Шаблон титульного листа не редактируется в Phase 9 — переносим как есть (ADR §16.51).</summary>
    private readonly string? _titlePageTemplate;

    private bool _reacting;

    public ProfileEditorViewModel(ReportTemplate template, GostStyleProfile profile)
    {
        TemplateId = template.Id;
        IsFactoryDefault = template.GostVariant == "7.32-2017";
        _titlePageTemplate = profile.TitlePageTemplate;

        _name = template.Name;
        _fontFamily = profile.FontFamily;
        _fontSizePt = profile.FontSizePt;
        _lineSpacing = profile.LineSpacing;
        _marginLeft = profile.Margins.Left;
        _marginRight = profile.Margins.Right;
        _marginTop = profile.Margins.Top;
        _marginBottom = profile.Margins.Bottom;
        _paragraphIndentCm = profile.ParagraphIndentCm;
        _bulletMarker = profile.BulletMarker;
        _selectedBodyAlignment = ReportForgeChoices.Alignments.FirstOrDefault(a => a.Value == profile.BodyAlignment)
                                 ?? ReportForgeChoices.Alignments[^1];

        _pageNumberingEnabled = profile.PageNumbering.Enabled;
        _selectedPageNumberPosition = ReportForgeChoices.PageNumberPositions
                                          .FirstOrDefault(p => p.Value == profile.PageNumbering.Position)
                                      ?? ReportForgeChoices.PageNumberPositions[0];
        _skipTitlePage = profile.PageNumbering.SkipTitlePage;

        _tocMinLevel = profile.TableOfContents.MinLevel;
        _tocMaxLevel = profile.TableOfContents.MaxLevel;

        var code = profile.CodeBlock ?? new CodeBlockStyleRule("Consolas", -2d, Boxed: true, BackgroundHex: null);
        _codeMonospaceFont = code.MonospaceFontFamily;
        _codeRelativeSize = code.RelativeFontSizePt;
        _codeBoxed = code.Boxed;
        _codeBackgroundHex = code.BackgroundHex ?? string.Empty;

        HeadingRules = [];
        foreach (var rule in profile.HeadingRules.OrderBy(r => r.Level))
        {
            HeadingRules.Add(new HeadingRuleRowViewModel(rule, RaiseHeadingPreview));
        }

        if (HeadingRules.Count == 0)
        {
            HeadingRules.Add(new HeadingRuleRowViewModel(
                new HeadingStyleRule(1, 16d, true, true, ParagraphAlignment.Center, true), RaiseHeadingPreview));
        }

        HeadingRules.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanRemoveHeadingLevel));
            OnPropertyChanged(nameof(CanAddHeadingLevel));
            RaiseHeadingPreview();
        };
    }

    /// <summary>Id шаблона, в который форма сохранится.</summary>
    public Guid TemplateId { get; }

    /// <summary>Правится ли заводской профиль — на всякий случай для подписи в форме.</summary>
    public bool IsFactoryDefault { get; }

    public string HeaderText => $"Профиль оформления «{Name}»";

    public ObservableCollection<HeadingRuleRowViewModel> HeadingRules { get; }

    public IReadOnlyList<NamedChoice<ParagraphAlignment>> Alignments => ReportForgeChoices.Alignments;

    public IReadOnlyList<NamedChoice<PageNumberPosition>> PageNumberPositions => ReportForgeChoices.PageNumberPositions;

    // ---- поля формы -----------------------------------------------------------------------------

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _fontFamily;

    [ObservableProperty]
    private double _fontSizePt;

    [ObservableProperty]
    private double _lineSpacing;

    [ObservableProperty]
    private double _marginLeft;

    [ObservableProperty]
    private double _marginRight;

    [ObservableProperty]
    private double _marginTop;

    [ObservableProperty]
    private double _marginBottom;

    [ObservableProperty]
    private double _paragraphIndentCm;

    [ObservableProperty]
    private string _bulletMarker;

    [ObservableProperty]
    private NamedChoice<ParagraphAlignment> _selectedBodyAlignment;

    [ObservableProperty]
    private bool _pageNumberingEnabled;

    [ObservableProperty]
    private NamedChoice<PageNumberPosition> _selectedPageNumberPosition;

    [ObservableProperty]
    private bool _skipTitlePage;

    [ObservableProperty]
    private int _tocMinLevel;

    [ObservableProperty]
    private int _tocMaxLevel;

    [ObservableProperty]
    private string _codeMonospaceFont;

    [ObservableProperty]
    private double _codeRelativeSize;

    [ObservableProperty]
    private bool _codeBoxed;

    [ObservableProperty]
    private string _codeBackgroundHex;

    // ---- команды уровней заголовков -----------------------------------------------------------

    public bool CanAddHeadingLevel => HeadingRules.Count < MaxHeadingLevels;

    public bool CanRemoveHeadingLevel => HeadingRules.Count > 1;

    [RelayCommand]
    private void AddHeadingLevel()
    {
        if (!CanAddHeadingLevel)
        {
            return;
        }

        var previous = HeadingRules[^1].ToRule();
        var added = previous with { Level = HeadingRules.Count + 1, PageBreakBefore = false };
        HeadingRules.Add(new HeadingRuleRowViewModel(added, RaiseHeadingPreview));
    }

    [RelayCommand]
    private void RemoveHeadingLevel()
    {
        if (CanRemoveHeadingLevel)
        {
            HeadingRules.RemoveAt(HeadingRules.Count - 1);
        }
    }

    // ---- валидация и сборка модели -----------------------------------------------------------

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(FontFamily)
        && FontSizePt is >= 8d and <= 72d
        && LineSpacing is >= 1d and <= 3d
        && InMargin(MarginLeft) && InMargin(MarginRight) && InMargin(MarginTop) && InMargin(MarginBottom)
        && ParagraphIndentCm is >= 0d and <= 5d
        && TocMinLevel is >= 1 and <= 9
        && TocMaxLevel >= TocMinLevel && TocMaxLevel <= 9
        && !string.IsNullOrWhiteSpace(CodeMonospaceFont)
        && CodeRelativeSize is >= -8d and <= 8d;

    private static bool InMargin(double value) => value is >= 0d and <= 100d;

    public GostStyleProfile ToProfile() => new(
        FontFamily.Trim(),
        FontSizePt,
        LineSpacing,
        new MarginsMm(MarginLeft, MarginRight, MarginTop, MarginBottom),
        ParagraphIndentCm,
        SelectedBodyAlignment.Value,
        string.IsNullOrEmpty(BulletMarker) ? "–" : BulletMarker,
        HeadingRules.Select(r => r.ToRule()).ToList(),
        new PageNumberingOptions(PageNumberingEnabled, SelectedPageNumberPosition.Value, SkipTitlePage),
        new TocOptions(TocMinLevel, TocMaxLevel),
        _titlePageTemplate,
        new CodeBlockStyleRule(
            CodeMonospaceFont.Trim(),
            CodeRelativeSize,
            CodeBoxed,
            NormalizeHex(CodeBackgroundHex)));

    // ---- производные свойства для предпросмотра ---------------------------------------------

    public double PreviewPageWidthPx => 210d * PreviewScale;

    public double PreviewPageHeightPx => 297d * PreviewScale;

    public Thickness PreviewMarginPx => new(
        MarginLeft * PreviewScale,
        MarginTop * PreviewScale,
        MarginRight * PreviewScale,
        MarginBottom * PreviewScale);

    public string PreviewMarginsCaption => string.Create(
        CultureInfo.InvariantCulture,
        $"Поля: {MarginLeft:0.#} / {MarginRight:0.#} / {MarginTop:0.#} / {MarginBottom:0.#} мм");

    public double PreviewBodyIndentPx => ParagraphIndentCm * 10d * PreviewScale;

    public Thickness PreviewFirstLineIndent => new(PreviewBodyIndentPx, 0d, 0d, 0d);

    public TextAlignment PreviewBodyTextAlignment => ToTextAlignment(SelectedBodyAlignment.Value);

    public HeadingRuleRowViewModel PreviewHeading => HeadingRules[0];

    public double PreviewHeadingFontSize => Math.Max(6d, PreviewHeading.FontSizePt);

    public FontWeight PreviewHeadingWeight => PreviewHeading.Bold ? FontWeights.Bold : FontWeights.Normal;

    public TextAlignment PreviewHeadingTextAlignment => ToTextAlignment(PreviewHeading.SelectedAlignment.Value);

    public string PreviewHeadingSample =>
        PreviewHeading.UpperCase ? "1 ЗАГОЛОВОК РАЗДЕЛА" : "1 Заголовок раздела";

    public string PreviewBulletMarker => string.IsNullOrEmpty(BulletMarker) ? "–" : BulletMarker;

    public double PreviewCodeFontSize => Math.Max(8d, FontSizePt + CodeRelativeSize);

    public Brush PreviewCodeBackground =>
        NormalizeHex(CodeBackgroundHex) is { } hex && TryBrush(hex, out var brush)
            ? brush
            : Brushes.Transparent;

    public Thickness PreviewCodeBorderThickness => CodeBoxed ? new Thickness(1d) : new Thickness(0d);

    public bool PreviewPageNumberVisible => PageNumberingEnabled;

    public bool PreviewPageNumberAtTop =>
        SelectedPageNumberPosition.Value is PageNumberPosition.TopCenter or PageNumberPosition.TopRight;

    public bool PreviewPageNumberTopVisible => PageNumberingEnabled && PreviewPageNumberAtTop;

    public bool PreviewPageNumberBottomVisible => PageNumberingEnabled && !PreviewPageNumberAtTop;

    public HorizontalAlignment PreviewPageNumberAlignment =>
        SelectedPageNumberPosition.Value is PageNumberPosition.BottomRight or PageNumberPosition.TopRight
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Center;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_reacting || e.PropertyName is nameof(CanSave) or nameof(HeaderText))
        {
            return;
        }

        _reacting = true;
        try
        {
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(HeaderText));
            RaisePreview();
        }
        finally
        {
            _reacting = false;
        }
    }

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(PreviewMarginPx));
        OnPropertyChanged(nameof(PreviewMarginsCaption));
        OnPropertyChanged(nameof(PreviewBodyIndentPx));
        OnPropertyChanged(nameof(PreviewFirstLineIndent));
        OnPropertyChanged(nameof(PreviewBodyTextAlignment));
        OnPropertyChanged(nameof(PreviewBulletMarker));
        OnPropertyChanged(nameof(PreviewCodeFontSize));
        OnPropertyChanged(nameof(PreviewCodeBackground));
        OnPropertyChanged(nameof(PreviewCodeBorderThickness));
        OnPropertyChanged(nameof(PreviewPageNumberVisible));
        OnPropertyChanged(nameof(PreviewPageNumberAtTop));
        OnPropertyChanged(nameof(PreviewPageNumberTopVisible));
        OnPropertyChanged(nameof(PreviewPageNumberBottomVisible));
        OnPropertyChanged(nameof(PreviewPageNumberAlignment));
    }

    private void RaiseHeadingPreview()
    {
        OnPropertyChanged(nameof(PreviewHeading));
        OnPropertyChanged(nameof(PreviewHeadingFontSize));
        OnPropertyChanged(nameof(PreviewHeadingWeight));
        OnPropertyChanged(nameof(PreviewHeadingTextAlignment));
        OnPropertyChanged(nameof(PreviewHeadingSample));
    }

    private static TextAlignment ToTextAlignment(ParagraphAlignment alignment) => alignment switch
    {
        ParagraphAlignment.Center => TextAlignment.Center,
        ParagraphAlignment.Right => TextAlignment.Right,
        ParagraphAlignment.Justify => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static string? NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var trimmed = hex.Trim().TrimStart('#');
        return trimmed.Length == 6 && trimmed.All(Uri.IsHexDigit) ? trimmed.ToUpperInvariant() : null;
    }

    private static bool TryBrush(string rrggbb, out Brush brush)
    {
        try
        {
            var value = int.Parse(rrggbb, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            brush = new SolidColorBrush(Color.FromRgb(
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)));
            brush.Freeze();
            return true;
        }
        catch (FormatException)
        {
            brush = Brushes.Transparent;
            return false;
        }
    }
}
