using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Organizer;

/// <summary>
/// Строка сетки звонков в редакторе семестра. Время правится текстом (как в редакторе пары),
/// чтобы не тащить в форму отдельный контрол выбора времени.
/// </summary>
public sealed partial class PairSlotRowViewModel : ObservableObject
{
    // "hh" в .NET — 12-часовой формат без AM/PM: час 13 печатался как «01» (new_addons.md §8).
    private const string TimeFormat = @"HH\:mm";

    public PairSlotRowViewModel(PairSlot slot)
    {
        _order = slot.Order;
        _startText = slot.Start.ToString(TimeFormat, CultureInfo.InvariantCulture);
        _endText = slot.End.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private string _startText;

    [ObservableProperty]
    private string _endText;

    /// <summary>Разобранный слот либо <see langword="null"/>, если время введено неверно.</summary>
    public PairSlot? ToModel()
    {
        if (!TryParseTime(StartText, out var start) || !TryParseTime(EndText, out var end) || end <= start)
        {
            return null;
        }

        return new PairSlot(Order, start, end);
    }

    internal static bool TryParseTime(string? text, out TimeOnly value) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
        || TimeOnly.TryParse(text, CultureInfo.CurrentCulture, out value);
}

/// <summary>
/// Форма семестра (new_addons.md §5): название, курс, даты начала и конца, чётность первой недели
/// и сетка звонков. Создаётся вручную из настроек, в DI не регистрируется (ADR §16.24).
/// </summary>
public sealed partial class SemesterEditorViewModel : ObservableObject
{
    // "hh" в .NET — 12-часовой формат без AM/PM: час 13 печатался как «01» (new_addons.md §8).
    private const string TimeFormat = @"HH\:mm";

    private readonly Guid _id;

    /// <summary>Конструктор для дизайнера XAML.</summary>
    public SemesterEditorViewModel()
        : this(null)
    {
    }

    public SemesterEditorViewModel(Semester? existing)
    {
        _id = existing?.Id ?? Guid.Empty;
        IsEditMode = existing is not null;

        _name = existing?.Name ?? string.Empty;
        _courseNumber = existing?.CourseNumber ?? 1;
        _startDate = existing is not null
            ? existing.StartDate.ToDateTime(TimeOnly.MinValue)
            : DateTime.Today;
        _endDate = existing?.EndDate?.ToDateTime(TimeOnly.MinValue);
        _firstWeekIsOdd = existing?.FirstWeekIsOdd ?? true;
        _defaultBreakMinutes = existing?.DefaultBreakMinutes ?? 10;
        _lunchBreakStartText = existing?.LunchBreakStart is { } lunchStart
            ? lunchStart.ToString(TimeFormat, CultureInfo.InvariantCulture)
            : string.Empty;
        _lunchBreakEndText = existing?.LunchBreakEnd is { } lunchEnd
            ? lunchEnd.ToString(TimeFormat, CultureInfo.InvariantCulture)
            : string.Empty;

        foreach (var slot in PairSlots.Parse(existing?.PairSlotsJson))
        {
            Slots.Add(Track(new PairSlotRowViewModel(slot)));
        }
    }

    public bool IsEditMode { get; }

    public string HeaderText => IsEditMode ? "Изменить семестр" : "Новый семестр";

    /// <summary>Сетка звонков семестра; пустая сетка допустима.</summary>
    public ObservableCollection<PairSlotRowViewModel> Slots { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private int _courseNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DateTime _startDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DateTime? _endDate;

    [ObservableProperty]
    private bool _firstWeekIsOdd;

    /// <summary>Обычный перерыв между парами, минуты — используется подсказкой времени новой пары
    /// (new_addons.md §5.1), а не только генератором сетки ниже.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private int _defaultBreakMinutes;

    /// <summary>Начало обеденного окна; пусто — не задано (new_addons.md §5.1).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private string _lunchBreakStartText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private string _lunchBreakEndText;

    // Параметры генератора сетки звонков. Ничего не выдумываем за пользователя: он сам задаёт
    // время первой пары, длительность и перемену, генератор лишь размножает их.
    [ObservableProperty]
    private string _generatorStartText = "08:00";

    [ObservableProperty]
    private int _generatorDurationMinutes = 90;

    [ObservableProperty]
    private int _generatorBreakMinutes = 10;

    [ObservableProperty]
    private int _generatorCount = 6;

    /// <summary>
    /// Дата окончания обязательна для нового семестра: без неё нельзя посчитать часы, а подставлять
    /// «примерно 18 недель» — значит выдумать учебный календарь. Исключение — семестр, перенесённый
    /// из старых настроек: у него даты конца никогда не было, и его сохранение блокировать нельзя.
    /// </summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name)
        && CourseNumber is >= 1 and <= 12
        && (EndDate is null ? IsEditMode : EndDate > StartDate)
        && DefaultBreakMinutes >= 0
        && IsLunchBreakValid;

    /// <summary>Причина, по которой сохранение недоступно; пусто — форма валидна (общая конвенция
    /// формы, new_addons.md §6.1).</summary>
    public string ValidationHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Укажите название семестра";
            }

            if (CourseNumber is < 1 or > 12)
            {
                return "Курс должен быть от 1 до 12";
            }

            if (EndDate is not null && EndDate <= StartDate)
            {
                return "Дата окончания должна быть позже даты начала";
            }

            if (EndDate is null && !IsEditMode)
            {
                return "Укажите дату окончания семестра";
            }

            if (DefaultBreakMinutes < 0)
            {
                return "Перерыв между парами не может быть отрицательным";
            }

            return IsLunchBreakValid
                ? string.Empty
                : "Обеденный перерыв: укажите оба времени и конец позже начала, либо оставьте оба поля пустыми";
        }
    }

    /// <summary>Оба поля обеда пусты (перерыв не задан), либо оба распознаны и конец позже начала.</summary>
    private bool IsLunchBreakValid
    {
        get
        {
            var startEmpty = string.IsNullOrWhiteSpace(LunchBreakStartText);
            var endEmpty = string.IsNullOrWhiteSpace(LunchBreakEndText);
            if (startEmpty && endEmpty)
            {
                return true;
            }

            return !startEmpty && !endEmpty
                && PairSlotRowViewModel.TryParseTime(LunchBreakStartText, out var start)
                && PairSlotRowViewModel.TryParseTime(LunchBreakEndText, out var end)
                && end > start;
        }
    }

    [RelayCommand]
    private void AddSlot()
    {
        var nextOrder = Slots.Count == 0 ? 1 : Slots.Max(s => s.Order) + 1;
        var start = Slots.Count == 0
            ? new TimeOnly(8, 0)
            : (Slots[^1].ToModel()?.End ?? new TimeOnly(8, 0)).AddMinutes(GeneratorBreakMinutes);

        Slots.Add(Track(new PairSlotRowViewModel(
            new PairSlot(nextOrder, start, start.AddMinutes(GeneratorDurationMinutes)))));
    }

    [RelayCommand]
    private void RemoveSlot(PairSlotRowViewModel? row)
    {
        if (row is not null)
        {
            Slots.Remove(row);
        }
    }

    /// <summary>Строит сетку из введённых пользователем параметров, заменяя текущую.</summary>
    [RelayCommand]
    private void GenerateSlots()
    {
        if (!PairSlotRowViewModel.TryParseTime(GeneratorStartText, out var start)
            || GeneratorDurationMinutes <= 0
            || GeneratorCount <= 0)
        {
            return;
        }

        Slots.Clear();
        var cursor = start;
        for (var i = 1; i <= GeneratorCount; i++)
        {
            var end = cursor.AddMinutes(GeneratorDurationMinutes);
            Slots.Add(Track(new PairSlotRowViewModel(new PairSlot(i, cursor, end))));
            cursor = end.AddMinutes(Math.Max(0, GeneratorBreakMinutes));
        }
    }

    public Semester ToModel() => new()
    {
        Id = _id,
        Name = Name.Trim(),
        CourseNumber = CourseNumber,
        StartDate = DateOnly.FromDateTime(StartDate),
        EndDate = EndDate is { } end ? DateOnly.FromDateTime(end) : null,
        FirstWeekIsOdd = FirstWeekIsOdd,
        PairSlotsJson = PairSlots.Serialize(
            [.. Slots.Select(s => s.ToModel()).Where(s => s is not null).Select(s => s!.Value)]),
        DefaultBreakMinutes = DefaultBreakMinutes,
        LunchBreakStart = PairSlotRowViewModel.TryParseTime(LunchBreakStartText, out var lunchStart)
            ? lunchStart
            : null,
        LunchBreakEnd = PairSlotRowViewModel.TryParseTime(LunchBreakEndText, out var lunchEnd)
            ? lunchEnd
            : null,
    };

    /// <summary>Правка строки слота должна пересчитывать <see cref="CanSave"/> и подсказки формы.</summary>
    private PairSlotRowViewModel Track(PairSlotRowViewModel row)
    {
        row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanSave));
        return row;
    }
}
