using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudComp.ViewModels.Organizer;

/// <summary>Маркер дедлайна в клетке календаря — точка цвета предмета.</summary>
public sealed record CalendarMarker(Brush Brush, bool IsOverdue, string Tooltip);

/// <summary>Одна клетка месячной сетки.</summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    public CalendarDayViewModel(DateOnly date, bool isCurrentMonth, IReadOnlyList<CalendarMarker> markers)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsToday = date == DateOnly.FromDateTime(DateTime.Today);
        DayText = date.Day.ToString(CultureInfo.InvariantCulture);

        // Больше трёх точек в клетке не помещается — остаток показываем счётчиком.
        Markers = [.. markers.Take(3)];
        OverflowText = markers.Count > 3 ? $"+{markers.Count - 3}" : string.Empty;
        HasOverflow = OverflowText.Length > 0;
        HasMarkers = markers.Count > 0;
    }

    public DateOnly Date { get; }

    public string DayText { get; }

    public bool IsCurrentMonth { get; }

    public bool IsToday { get; }

    public IReadOnlyList<CalendarMarker> Markers { get; }

    public bool HasMarkers { get; }

    public string OverflowText { get; }

    public bool HasOverflow { get; }

    /// <summary>Выбранный день — по нему фильтруется список справа.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Календарный вид дедлайнов (new_addons.md §5): месячная сетка 7×6 с точками-маркерами по
/// предметам; клик по дню фильтрует список.
/// </summary>
/// <remarks>
/// Своя сетка вместо календарного контрола: нужного в WPF-UI нет, а пакеты в проекте заморожены —
/// тот же довод, что у <c>GradeTrendChart</c> (ADR §16.61). Сетка простая: 42 клетки без анимаций.
/// </remarks>
public sealed partial class DeadlineCalendarViewModel : ObservableObject
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private IReadOnlyList<DeadlineRowViewModel> _source = [];

    public DeadlineCalendarViewModel()
    {
        var today = DateTime.Today;
        _month = new DateOnly(today.Year, today.Month, 1);
    }

    /// <summary>Подписи дней недели, понедельник первым.</summary>
    public IReadOnlyList<string> WeekDayNames { get; } =
        [.. OrganizerChoices.Days.Select(d => d.Display[..2].ToUpperInvariant())];

    public ObservableCollection<CalendarDayViewModel> Days { get; } = [];

    [ObservableProperty]
    private DateOnly _month;

    [ObservableProperty]
    private string _monthText = string.Empty;

    /// <summary>Выбранный день; <see langword="null"/> — показываем весь месяц.</summary>
    [ObservableProperty]
    private DateOnly? _selectedDate;

    /// <summary>Пользователь выбрал день — список дедлайнов перестраивается.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Пересобрать сетку по актуальному списку дедлайнов.</summary>
    public void Rebuild(IReadOnlyList<DeadlineRowViewModel> deadlines)
    {
        _source = deadlines;

        MonthText = Month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Ru);
        MonthText = char.ToUpper(MonthText[0], Ru) + MonthText[1..];

        // Сетка всегда начинается с понедельника недели, в которую попало 1-е число.
        var first = new DateOnly(Month.Year, Month.Month, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));

        var byDate = deadlines
            .GroupBy(x => DateOnly.FromDateTime(x.DueDate.LocalDateTime.Date))
            .ToDictionary(g => g.Key, g => g.ToArray());

        Days.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var markers = byDate.TryGetValue(date, out var items)
                ? items.Select(x => new CalendarMarker(x.AccentBrush, x.IsOverdue, x.Title)).ToArray()
                : [];

            Days.Add(new CalendarDayViewModel(date, date.Month == Month.Month, markers)
            {
                IsSelected = date == SelectedDate,
            });
        }
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        Month = Month.AddMonths(-1);
        Rebuild(_source);
    }

    [RelayCommand]
    private void NextMonth()
    {
        Month = Month.AddMonths(1);
        Rebuild(_source);
    }

    [RelayCommand]
    private void GoToday()
    {
        // Выбираем сегодня, а не снимаем фильтр — SelectDay(null) для этого не подходит: его
        // toggle-семантика (повторный клик по уже выбранному дню снимает выбор) инвертировала бы
        // повторное нажатие «Сегодня», если сегодняшний день уже выбран (new_addons.md §7.3).
        SelectedDate = DateOnly.FromDateTime(DateTime.Today);
        Month = new DateOnly(SelectedDate.Value.Year, SelectedDate.Value.Month, 1);
        Rebuild(_source);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectDay(CalendarDayViewModel? day)
    {
        // Повторный клик по выбранному дню снимает фильтр — возвращаемся ко всему месяцу.
        SelectedDate = day is null || day.Date == SelectedDate ? null : day.Date;

        foreach (var item in Days)
        {
            item.IsSelected = item.Date == SelectedDate;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Отфильтровать список по выбранному дню (или вернуть его целиком).</summary>
    public IReadOnlyList<DeadlineRowViewModel> Filter(IReadOnlyList<DeadlineRowViewModel> deadlines) =>
        SelectedDate is { } date
            ? [.. deadlines.Where(x => DateOnly.FromDateTime(x.DueDate.LocalDateTime.Date) == date)]
            : deadlines;
}
