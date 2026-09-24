using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.ViewModels;

namespace StudComp.ViewModels.Organizer;

/// <summary>
/// Компактная форма создания пары кликом по пустой ячейке расписания (new_addons.md §5).
/// Открывается поповером прямо над ячейкой, закрывается по <c>Esc</c>, создаёт пару по <c>Enter</c>.
/// </summary>
public sealed partial class SchedulePopoverViewModel : ObservableObject
{
    // "hh" в .NET — 12-часовой формат без AM/PM: час 13 печатался как «01» (new_addons.md §8).
    private const string TimeFormat = @"HH\:mm";

    private DayOfWeek _day;

    /// <summary>Предметы для поиска; в списке — только предметы активного семестра.</summary>
    public ObservableCollection<Subject> Subjects { get; } = [];

    public IReadOnlyList<NamedChoice<ScheduleEntryType>> Types => OrganizerChoices.ScheduleTypes;

    public IReadOnlyList<NamedChoice<WeekParity>> Parities => OrganizerChoices.Parities;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Подпись «Понедельник, 09:00» — куда именно кликнули.</summary>
    [ObservableProperty]
    private string _header = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private NamedChoice<ScheduleEntryType> _selectedType = OrganizerChoices.ScheduleTypes[0];

    [ObservableProperty]
    private NamedChoice<WeekParity> _selectedParity = OrganizerChoices.Parities[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private string _startTimeText = "09:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private string _endTimeText = "10:30";

    [ObservableProperty]
    private string _room = string.Empty;

    [ObservableProperty]
    private string _teacher = string.Empty;

    /// <summary>Преподаватель подставлен из предмета и заблокирован для правки (new_addons.md §5.2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTeacherEditable))]
    private bool _isTeacherLocked;

    /// <summary>Инверсия <see cref="IsTeacherLocked"/> для биндинга <c>IsEnabled</c>.</summary>
    public bool IsTeacherEditable => !IsTeacherLocked;

    /// <summary>Подсказка про сетку звонков — откуда взялось предзаполненное время.</summary>
    [ObservableProperty]
    private string _slotHint = string.Empty;

    public bool CanSave =>
        SelectedSubject is not null
        && TryParseTime(StartTimeText, out var start)
        && TryParseTime(EndTimeText, out var end)
        && end > start;

    /// <summary>Причина, по которой сохранение недоступно; пусто — форма валидна (new_addons.md
    /// §6.1, Тест.txt №4) — общая подсказка вместо молчаливо задизейбленной кнопки.</summary>
    public string ValidationHint
    {
        get
        {
            if (SelectedSubject is null)
            {
                return "Выберите предмет";
            }

            if (!TryParseTime(StartTimeText, out var start))
            {
                return "Время начала указано неверно";
            }

            if (!TryParseTime(EndTimeText, out var end))
            {
                return "Время конца указано неверно";
            }

            return end <= start ? "Время окончания должно быть позже начала" : string.Empty;
        }
    }

    /// <summary>Пользователь подтвердил создание пары.</summary>
    public event EventHandler? Submitted;

    /// <summary>Пользователь попросил завести новый предмет прямо из формы.</summary>
    public event EventHandler? NewSubjectRequested;

    /// <summary>
    /// Открыть форму для кликнутой ячейки. Если клик пришёлся на или после конца последней пары этого
    /// дня — время предлагается как «конец предыдущей пары + перерыв» с учётом обеденного окна
    /// (new_addons.md §5.1). Иначе — из сетки звонков семестра, а если её нет — из самой ячейки плюс
    /// полтора часа (та же длительность по умолчанию, что и в полном редакторе).
    /// </summary>
    public void Open(
        DayOfWeek day,
        TimeOnly cellTime,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<PairSlot> slots,
        TimeSpan defaultDuration,
        IReadOnlyList<ScheduleEntry> sameDayEntries,
        Semester? semester)
    {
        _day = day;

        Subjects.Clear();
        foreach (var subject in subjects)
        {
            Subjects.Add(subject);
        }

        SelectedSubject = null;
        SelectedType = OrganizerChoices.ScheduleTypes[0];
        SelectedParity = OrganizerChoices.Parities[0];
        Room = string.Empty;
        Teacher = string.Empty;
        IsTeacherLocked = false;

        var lastEnd = sameDayEntries.Count > 0 ? sameDayEntries.Max(e => e.EndTime) : (TimeOnly?)null;
        if (lastEnd is { } end && cellTime >= end)
        {
            var suggestion = NextPairTimeCalculator.Suggest(
                sameDayEntries,
                defaultDuration,
                TimeSpan.FromMinutes(semester?.DefaultBreakMinutes ?? 10),
                NextPairTimeCalculator.LunchOf(semester),
                cellTime);

            StartTimeText = suggestion.Start.ToString(TimeFormat, CultureInfo.InvariantCulture);
            EndTimeText = suggestion.End.ToString(TimeFormat, CultureInfo.InvariantCulture);
            SlotHint = "Предложено по концу предыдущей пары";
        }
        else
        {
            var slot = PairSlots.SlotFor(slots, cellTime);
            if (slot is { } pair)
            {
                StartTimeText = pair.Start.ToString(TimeFormat, CultureInfo.InvariantCulture);
                EndTimeText = pair.End.ToString(TimeFormat, CultureInfo.InvariantCulture);
                SlotHint = $"Пара {pair.Order} по сетке звонков";
            }
            else
            {
                StartTimeText = cellTime.ToString(TimeFormat, CultureInfo.InvariantCulture);
                EndTimeText = cellTime.Add(defaultDuration).ToString(TimeFormat, CultureInfo.InvariantCulture);
                SlotHint = "Сетка звонков не задана — время можно поправить вручную";
            }
        }

        Header = $"{OrganizerChoices.DayName(day)}, {StartTimeText}";
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Submit() => Submitted?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RequestNewSubject() => NewSubjectRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>«Указать другого преподавателя для этой пары» — снимает блокировку, текст не трогает.</summary>
    [RelayCommand]
    private void UnlockTeacher() => IsTeacherLocked = false;

    partial void OnSelectedSubjectChanged(Subject? value)
    {
        SubmitCommand.NotifyCanExecuteChanged();

        if (!string.IsNullOrWhiteSpace(value?.TeacherFullName))
        {
            Teacher = value!.TeacherFullName!;
            IsTeacherLocked = true;
        }
        else
        {
            IsTeacherLocked = false;
        }
    }

    partial void OnStartTimeTextChanged(string value)
    {
        Header = $"{OrganizerChoices.DayName(_day)}, {value}";
        SubmitCommand.NotifyCanExecuteChanged();
    }

    partial void OnEndTimeTextChanged(string value) => SubmitCommand.NotifyCanExecuteChanged();

    /// <summary>Собрать пару из формы. Зовётся только когда <see cref="CanSave"/>.</summary>
    public ScheduleEntry ToModel() => new()
    {
        SubjectId = SelectedSubject!.Id,
        DayOfWeek = _day,
        StartTime = Parse(StartTimeText),
        EndTime = Parse(EndTimeText),
        Room = Room?.Trim() ?? string.Empty,
        Teacher = Teacher?.Trim() ?? string.Empty,
        Type = SelectedType.Value,
        WeekParity = SelectedParity.Value,
    };

    private static TimeOnly Parse(string text) => TryParseTime(text, out var value) ? value : default;

    private static bool TryParseTime(string? text, out TimeOnly value) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
        || TimeOnly.TryParse(text, CultureInfo.CurrentCulture, out value);
}
