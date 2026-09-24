using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Organizer;

/// <summary>
/// Плитка одной пары в сетке расписания: позиция в сетке (колонка дня, строка и высота по времени),
/// дорожка внутри дня и данные для отображения.
/// </summary>
/// <remarks>
/// <see cref="ObservableObject"/> — плитку двигают и растягивают мышью, и она должна ехать за
/// курсором до того, как правка доедет до БД (new_addons.md §5).
/// </remarks>
public sealed partial class ScheduleTileViewModel : ObservableObject
{
    private readonly int _dayStartHour;
    private readonly int _slotMinutes;

    public ScheduleTileViewModel(
        ScheduleEntry entry,
        string subjectName,
        string? subjectColorHex,
        int dayStartHour,
        int slotMinutes,
        int lane = 0,
        int laneCount = 1)
    {
        _dayStartHour = dayStartHour;
        _slotMinutes = slotMinutes;

        Entry = entry;
        SubjectName = subjectName;
        AccentBrush = SubjectColor.BrushFor(subjectColorHex);
        FillBrush = SubjectColor.TintFor(subjectColorHex);

        Lane = lane;
        LaneCount = Math.Max(1, laneCount);

        _dayColumn = ((int)entry.DayOfWeek + 6) % 7;
        var placement = Place(entry.StartTime, entry.EndTime);
        _rowOffset = placement.Offset;
        _rowSpan = placement.Span;

        IsToday = entry.DayOfWeek == DateTime.Today.DayOfWeek;
        TypeText = OrganizerChoices.ScheduleTypeName(entry.Type);
        ParityText = entry.WeekParity switch
        {
            WeekParity.Odd => "числ.",
            WeekParity.Even => "знам.",
            _ => string.Empty,
        };

        _timeRange = FormatRange(entry.StartTime, entry.EndTime);
    }

    public ScheduleEntry Entry { get; }

    public string SubjectName { get; }

    /// <summary>Цвет предмета — полоса слева.</summary>
    public Brush AccentBrush { get; }

    /// <summary>Приглушённая заливка того же цвета — фон плитки.</summary>
    public Brush FillBrush { get; }

    /// <summary>Номер дорожки внутри дня (режим «обе недели»).</summary>
    public int Lane { get; }

    /// <summary>Сколько дорожек делят ширину дня.</summary>
    public int LaneCount { get; }

    /// <summary>Колонка дня, понедельник = 0.</summary>
    [ObservableProperty]
    private int _dayColumn;

    /// <summary>Смещение верхнего края в строках сетки (по времени начала).</summary>
    [ObservableProperty]
    private int _rowOffset;

    /// <summary>Высота в строках сетки (по длительности).</summary>
    [ObservableProperty]
    private int _rowSpan;

    [ObservableProperty]
    private string _timeRange;

    /// <summary>Пара идёт прямо сейчас — подсвечивается в сетке.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    public bool IsToday { get; }

    public string TypeText { get; }

    /// <summary>Короткая пометка чётности: «числ.» / «знам.»; пусто — пара каждую неделю.</summary>
    public string ParityText { get; }

    public bool HasParity => ParityText.Length > 0;

    public string Room => Entry.Room;

    public string Teacher => Entry.Teacher;

    /// <summary>Время начала, соответствующее строке сетки.</summary>
    public TimeOnly TimeForRow(int row) => new TimeOnly(_dayStartHour, 0).AddMinutes(row * _slotMinutes);

    /// <summary>Длительность, соответствующая высоте в строках.</summary>
    public TimeSpan DurationForSpan(int span) => TimeSpan.FromMinutes(Math.Max(1, span) * _slotMinutes);

    /// <summary>Переставить плитку на новое место до того, как правка доедет до БД.</summary>
    public void PreviewPlacement(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        DayColumn = ((int)day + 6) % 7;

        var placement = Place(start, end);
        RowOffset = placement.Offset;
        RowSpan = placement.Span;
        TimeRange = FormatRange(start, end);
    }

    private (int Offset, int Span) Place(TimeOnly start, TimeOnly end)
    {
        var startMinutes = (int)(start - new TimeOnly(_dayStartHour, 0)).TotalMinutes;
        var spanMinutes = (int)(end - start).TotalMinutes;

        return (
            Math.Max(0, startMinutes / _slotMinutes),
            Math.Max(1, (int)Math.Ceiling(spanMinutes / (double)_slotMinutes)));
    }

    private static string FormatRange(TimeOnly start, TimeOnly end) =>
        start.ToString("HH\\:mm", CultureInfo.InvariantCulture)
        + "–" + end.ToString("HH\\:mm", CultureInfo.InvariantCulture);
}
