using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.ViewModels;

namespace StudComp.ViewModels.Organizer;

/// <summary>Форма создания/редактирования пары в расписании (диалог).</summary>
public sealed partial class ScheduleEntryEditorViewModel : ObservableObject
{
    // "hh" в .NET — 12-часовой формат без AM/PM: час 13 печатался как «01» (new_addons.md §8).
    private const string TimeFormat = @"HH\:mm";
    private readonly Guid _id;

    public ScheduleEntryEditorViewModel(
        IReadOnlyList<Subject> subjects,
        ScheduleEntry? existing,
        Guid? presetSubjectId = null,
        DayOfWeek? presetDay = null,
        (TimeOnly Start, TimeOnly End)? suggestedTime = null)
    {
        Subjects = subjects;
        _id = existing?.Id ?? Guid.Empty;
        IsEditMode = existing is not null;

        var subjectId = existing?.SubjectId ?? presetSubjectId;
        _selectedSubject = subjects.FirstOrDefault(s => s.Id == subjectId) ?? subjects.FirstOrDefault();
        _selectedDay = OrganizerChoices.Days.FirstOrDefault(
            d => d.Value == (existing?.DayOfWeek ?? presetDay ?? DayOfWeek.Monday))!;
        _selectedType = OrganizerChoices.ScheduleTypes.FirstOrDefault(t => t.Value == (existing?.Type ?? ScheduleEntryType.Lecture))!;
        _selectedParity = OrganizerChoices.Parities.FirstOrDefault(p => p.Value == (existing?.WeekParity ?? WeekParity.Any))!;
        _startTimeText = (existing?.StartTime ?? suggestedTime?.Start ?? new TimeOnly(9, 0))
            .ToString(TimeFormat, CultureInfo.InvariantCulture);
        _endTimeText = (existing?.EndTime ?? suggestedTime?.End ?? new TimeOnly(10, 30))
            .ToString(TimeFormat, CultureInfo.InvariantCulture);
        _room = existing?.Room ?? string.Empty;
        _teacher = existing?.Teacher ?? string.Empty;

        RecomputeTeacherLock(isInitial: true);
    }

    public bool IsEditMode { get; }

    public string HeaderText => IsEditMode ? "Изменить пару" : "Новая пара";

    public IReadOnlyList<Subject> Subjects { get; }

    public IReadOnlyList<NamedChoice<DayOfWeek>> Days => OrganizerChoices.Days;

    public IReadOnlyList<NamedChoice<ScheduleEntryType>> Types => OrganizerChoices.ScheduleTypes;

    public IReadOnlyList<NamedChoice<WeekParity>> Parities => OrganizerChoices.Parities;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private NamedChoice<DayOfWeek> _selectedDay;

    [ObservableProperty]
    private NamedChoice<ScheduleEntryType> _selectedType;

    [ObservableProperty]
    private NamedChoice<WeekParity> _selectedParity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private string _startTimeText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ValidationHint))]
    private string _endTimeText;

    [ObservableProperty]
    private string _room;

    [ObservableProperty]
    private string _teacher;

    /// <summary>Преподаватель подставлен из предмета и заблокирован для правки (new_addons.md §5.2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTeacherEditable))]
    private bool _isTeacherLocked;

    /// <summary>Инверсия <see cref="IsTeacherLocked"/> для биндинга <c>IsEnabled</c>.</summary>
    public bool IsTeacherEditable => !IsTeacherLocked;

    /// <summary>Форма валидна: выбран предмет, время распознано и окончание позже начала.</summary>
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

    /// <summary>Собрать доменную модель. Вызывать только когда <see cref="CanSave"/> истинно.</summary>
    public ScheduleEntry ToModel()
    {
        TryParseTime(StartTimeText, out var start);
        TryParseTime(EndTimeText, out var end);

        return new ScheduleEntry
        {
            Id = _id,
            SubjectId = SelectedSubject!.Id,
            DayOfWeek = SelectedDay.Value,
            StartTime = start,
            EndTime = end,
            Room = Room?.Trim() ?? string.Empty,
            Teacher = Teacher?.Trim() ?? string.Empty,
            Type = SelectedType.Value,
            WeekParity = SelectedParity.Value,
        };
    }

    private static bool TryParseTime(string? text, out TimeOnly value) =>
        TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out value)
        || TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>«Указать другого преподавателя для этой пары» — снимает блокировку, текст не трогает.</summary>
    [RelayCommand]
    private void UnlockTeacher() => IsTeacherLocked = false;

    partial void OnSelectedSubjectChanged(Subject? value) => RecomputeTeacherLock(isInitial: false);

    /// <summary>
    /// Пересчитать блокировку поля «Преподаватель» по предмету (new_addons.md §5.2). При открытии уже
    /// существующей пары, где преподавателя когда-то переопределили вручную (текст не совпадает с
    /// преподавателем предмета), блокировку не ставим — оставляем как было.
    /// </summary>
    private void RecomputeTeacherLock(bool isInitial)
    {
        var defaultTeacher = SelectedSubject?.TeacherFullName;
        if (string.IsNullOrWhiteSpace(defaultTeacher))
        {
            IsTeacherLocked = false;
            return;
        }

        if (isInitial && IsEditMode && !string.Equals(Teacher, defaultTeacher, StringComparison.Ordinal))
        {
            IsTeacherLocked = false;
            return;
        }

        Teacher = defaultTeacher;
        IsTeacherLocked = true;
    }
}
