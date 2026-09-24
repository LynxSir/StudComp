using System.Globalization;
using System.Windows.Media;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Organizer;

/// <summary>Строка списка зачётки: форматирует одну <see cref="GradeEntry"/> для карточки.</summary>
public sealed class GradeRowViewModel
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly SolidColorBrush PassBrush = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush FailBrush = Frozen(Color.FromRgb(0xC0, 0x37, 0x2A));
    private static readonly SolidColorBrush PlannedBrush = Frozen(Color.FromRgb(0x6B, 0x6B, 0x6B));

    public GradeRowViewModel(GradeEntry entry)
    {
        Entry = entry;
        IsPlanned = entry.IsPlanned;
        Title = string.IsNullOrWhiteSpace(entry.Title)
            ? OrganizerChoices.GradeTypes.First(t => t.Value == entry.Type).Display
            : entry.Title;
        TypeText = OrganizerChoices.GradeTypes.First(t => t.Value == entry.Type).Display;
        DateText = entry.Date.ToString("dd.MM.yyyy", Ru);
        WeightText = "вес " + entry.Weight.ToString("0.###", Ru);

        if (IsPlanned)
        {
            ScoreText = "запланировано";
            PercentText = "макс. " + entry.MaxScore.ToString("0.###", Ru);
            AccentBrush = PlannedBrush;
        }
        else
        {
            var fraction = entry.MaxScore > 0m ? entry.RawScore / entry.MaxScore : 0m;
            ScoreText = $"{entry.RawScore.ToString("0.###", Ru)} / {entry.MaxScore.ToString("0.###", Ru)}";
            PercentText = (fraction * 100m).ToString("0.#", Ru) + " %";
            AccentBrush = fraction >= 0.5m ? PassBrush : FailBrush;
        }
    }

    public GradeEntry Entry { get; }

    public bool IsPlanned { get; }

    public string Title { get; }

    public string TypeText { get; }

    public string DateText { get; }

    public string WeightText { get; }

    public string ScoreText { get; }

    public string PercentText { get; }

    public Brush AccentBrush { get; }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
