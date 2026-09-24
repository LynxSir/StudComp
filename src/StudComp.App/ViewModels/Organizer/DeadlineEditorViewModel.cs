using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.ViewModels;

namespace StudComp.ViewModels.Organizer;

/// <summary>Форма создания/редактирования дедлайна (диалог).</summary>
public sealed partial class DeadlineEditorViewModel : ObservableObject
{
    // "hh" в .NET — 12-часовой формат без AM/PM: час 13 печатался как «01» (new_addons.md §8).
    private const string TimeFormat = @"HH\:mm";
    private readonly Guid _id;
    private readonly DeadlineStatus _status;
    private readonly Guid? _linkedFileRecordId;

    // Папка дедлайна и дата сдачи живут вне формы, но UpdateAsync заменяет сущность целиком —
    // проносим их сквозь редактор, иначе правка заголовка потеряла бы папку с файлами.
    private readonly string? _folderName;
    private readonly DateTimeOffset? _answeredAt;

    public DeadlineEditorViewModel(IReadOnlyList<Subject> subjects, Deadline? existing, Guid? presetSubjectId = null)
    {
        Subjects = subjects;
        _id = existing?.Id ?? Guid.Empty;
        _status = existing?.Status ?? DeadlineStatus.Pending;
        _linkedFileRecordId = existing?.LinkedFileRecordId;
        _folderName = existing?.FolderName;
        _answeredAt = existing?.AnsweredAt;
        IsEditMode = existing is not null;

        var subjectId = existing?.SubjectId ?? presetSubjectId;
        _selectedSubject = subjects.FirstOrDefault(s => s.Id == subjectId) ?? subjects.FirstOrDefault();

        var due = existing?.DueDate.LocalDateTime ?? DateTime.Now.Date.AddDays(7).AddHours(23).AddMinutes(59);
        _dueDate = due.Date;
        _dueTimeText = TimeOnly.FromDateTime(due).ToString(TimeFormat, CultureInfo.InvariantCulture);
        _title = existing?.Title ?? string.Empty;
        _selectedType = OrganizerChoices.DeadlineTypes.FirstOrDefault(t => t.Value == (existing?.Type ?? DeadlineType.Homework))!;
        _selectedPriority = OrganizerChoices.Priorities.FirstOrDefault(p => p.Value == (existing?.Priority ?? DeadlinePriority.Normal))!;
    }

    public bool IsEditMode { get; }

    public string HeaderText => IsEditMode ? "Изменить дедлайн" : "Новый дедлайн";

    public IReadOnlyList<Subject> Subjects { get; }

    public IReadOnlyList<NamedChoice<DeadlineType>> Types => OrganizerChoices.DeadlineTypes;

    public IReadOnlyList<NamedChoice<DeadlinePriority>> Priorities => OrganizerChoices.Priorities;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private DateTime _dueDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _dueTimeText;

    [ObservableProperty]
    private NamedChoice<DeadlineType> _selectedType;

    [ObservableProperty]
    private NamedChoice<DeadlinePriority> _selectedPriority;

    /// <summary>Форма валидна: есть название, выбран предмет, время распознано.</summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Title)
        && SelectedSubject is not null
        && TryParseTime(DueTimeText, out _);

    /// <summary>Собрать доменную модель. Вызывать только когда <see cref="CanSave"/> истинно.</summary>
    public Deadline ToModel()
    {
        TryParseTime(DueTimeText, out var time);
        var localDue = DueDate.Date.Add(time.ToTimeSpan());

        return new Deadline
        {
            Id = _id,
            SubjectId = SelectedSubject!.Id,
            Title = Title.Trim(),
            DueDate = new DateTimeOffset(localDue, TimeZoneInfo.Local.GetUtcOffset(localDue)),
            Type = SelectedType.Value,
            Priority = SelectedPriority.Value,
            Status = _status,
            LinkedFileRecordId = _linkedFileRecordId,
            FolderName = _folderName,
            AnsweredAt = _answeredAt,
        };
    }

    private static bool TryParseTime(string? text, out TimeOnly value) =>
        TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out value)
        || TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
