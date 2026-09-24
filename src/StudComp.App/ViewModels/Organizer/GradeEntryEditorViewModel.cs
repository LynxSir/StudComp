using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.ViewModels;

namespace StudComp.ViewModels.Organizer;

/// <summary>Форма создания/редактирования строки зачётки (диалог). Строка бывает двух видов:
/// уже оценённая аттестация и запланированная (<see cref="IsPlanned"/>) — у неё балл не спрашивается.</summary>
public sealed partial class GradeEntryEditorViewModel : ObservableObject
{
    private readonly Guid _id;
    private readonly Guid _subjectId;

    public GradeEntryEditorViewModel()
        : this(Guid.Empty, null)
    {
    }

    public GradeEntryEditorViewModel(Guid subjectId, GradeEntry? existing)
    {
        _id = existing?.Id ?? Guid.Empty;
        _subjectId = existing?.SubjectId ?? subjectId;
        IsEditMode = existing is not null;

        _title = existing?.Title ?? string.Empty;
        _isPlanned = existing?.IsPlanned ?? false;
        _rawScoreText = existing is { IsPlanned: false } ? Format(existing.RawScore) : string.Empty;
        _maxScoreText = existing is not null ? Format(existing.MaxScore) : "100";
        _weightText = existing is not null ? Format(existing.Weight) : "1";
        _date = existing?.Date ?? DateTime.Today;
        _semester = existing?.Semester ?? 1;
        _selectedType = OrganizerChoices.GradeTypes
            .FirstOrDefault(t => t.Value == (existing?.Type ?? GradeEntryType.Attestation))!;
    }

    public bool IsEditMode { get; }

    public string HeaderText => IsEditMode ? "Изменить оценку" : "Новая оценка";

    public IReadOnlyList<NamedChoice<GradeEntryType>> Types => OrganizerChoices.GradeTypes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _title;

    [ObservableProperty]
    private NamedChoice<GradeEntryType> _selectedType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(IsScoreEnabled))]
    private bool _isPlanned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _rawScoreText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _maxScoreText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _weightText;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private int _semester;

    /// <summary>Поле «Набрано» активно только для уже сданной аттестации.</summary>
    public bool IsScoreEnabled => !IsPlanned;

    /// <summary>Название непустое, максимум и вес корректны, а для сданной — балл в пределах максимума.</summary>
    public bool CanSave
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                return false;
            }

            if (!TryParse(MaxScoreText, out var max) || max <= 0m)
            {
                return false;
            }

            if (!TryParse(WeightText, out var weight) || weight < 0m)
            {
                return false;
            }

            if (IsPlanned)
            {
                return true;
            }

            return TryParse(RawScoreText, out var raw) && raw >= 0m && raw <= max;
        }
    }

    /// <summary>Собрать доменную модель. Вызывать только когда <see cref="CanSave"/> истинно.</summary>
    public GradeEntry ToModel()
    {
        TryParse(MaxScoreText, out var max);
        TryParse(WeightText, out var weight);
        var raw = IsPlanned ? 0m : (TryParse(RawScoreText, out var parsed) ? parsed : 0m);

        return new GradeEntry
        {
            Id = _id,
            SubjectId = _subjectId,
            Type = SelectedType.Value,
            Title = Title.Trim(),
            IsPlanned = IsPlanned,
            RawScore = raw,
            MaxScore = max,
            Weight = weight,
            Date = Date.Date,
            Semester = Semester,
        };
    }

    private static string Format(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out decimal value)
    {
        text = text?.Trim().Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
