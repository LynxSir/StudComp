using System.Windows.Media;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Archivist;

/// <summary>Строка списка правил сортировки. Неизменяемая, как строки Органайзера.</summary>
public sealed class RuleRowViewModel
{
    private static readonly SolidColorBrush Fallback = Freeze(Color.FromRgb(0x8A, 0x1C, 0x2B));

    public RuleRowViewModel(ArchivistRule rule, Subject? subject)
    {
        Rule = rule;
        SubjectName = subject?.Name ?? "Без предмета";
        IsSubjectPlaceholder = subject is null;
        AccentBrush = BrushFor(subject?.ColorHex);
        ConditionText = rule.MatchType switch
        {
            RuleMatchType.Extension => $"Расширение: {rule.Pattern}",
            RuleMatchType.Regex => $"Выражение: {rule.Pattern}",
            _ => $"Слово в имени: {rule.Pattern}",
        };
        TemplateText = string.IsNullOrWhiteSpace(rule.RenameTemplate)
            ? "имя не меняется"
            : rule.RenameTemplate;
        PriorityText = $"приоритет {rule.Priority}";
        ScopeText = string.IsNullOrWhiteSpace(rule.WatchedFolder)
            ? "во всех папках"
            : $"только в {System.IO.Path.GetFileName(rule.WatchedFolder.TrimEnd('\\', '/'))}";
    }

    public ArchivistRule Rule { get; }

    public string SubjectName { get; }

    /// <summary>Правило без привязки к предмету — «Без предмета» показывается курсивом, не как обычное имя.</summary>
    public bool IsSubjectPlaceholder { get; }

    public Brush AccentBrush { get; }

    public string ConditionText { get; }

    public string TemplateText { get; }

    public string PriorityText { get; }

    /// <summary>Область действия правила — «во всех папках» либо имя конкретной наблюдаемой папки.</summary>
    public string ScopeText { get; }

    public bool IsEnabled => Rule.Enabled;

    private static Brush BrushFor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Fallback;
        }

        try
        {
            return ColorConverter.ConvertFromString(hex) is Color color ? Freeze(color) : Fallback;
        }
        catch (FormatException)
        {
            // Некорректный hex в данных — тихо уходим на дефолтный винный.
            return Fallback;
        }
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
