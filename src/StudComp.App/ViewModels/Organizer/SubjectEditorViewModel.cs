using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Services;
using StudComp.ViewModels;

namespace StudComp.ViewModels.Organizer;

/// <summary>Форма создания/редактирования предмета (диалог).</summary>
public sealed partial class SubjectEditorViewModel : ObservableObject
{
    private readonly Guid _id;
    private readonly IStudyWorkspace? _workspace;
    private readonly IDialogService? _dialogs;

    /// <summary>Пользователь правил имя папки руками — перестаём тянуть его за названием предмета.</summary>
    private bool _folderEditedByUser;

    /// <summary>Имя папки сейчас подставляет сама форма — это не правка пользователя.</summary>
    private bool _syncingFolderName;

    public SubjectEditorViewModel()
        : this(null, [], [])
    {
    }

    public SubjectEditorViewModel(
        Subject? existing,
        IReadOnlyList<ReportTemplate> reportTemplates,
        IReadOnlyList<Semester> semesters,
        Guid? defaultSemesterId = null,
        IStudyWorkspace? workspace = null,
        IDialogService? dialogs = null)
    {
        _id = existing?.Id ?? Guid.Empty;
        _workspace = workspace;
        _dialogs = dialogs;
        IsEditMode = existing is not null;
        _name = existing?.Name ?? string.Empty;
        _code = existing?.Code ?? string.Empty;
        _selectedAssessment = Assessments.First(x => x.Value == (existing?.Assessment ?? SubjectAssessment.Unspecified));
        _teacherFullName = existing?.TeacherFullName ?? string.Empty;
        var folder = ParseFolder(existing, workspace);
        _folderName = folder.Name;
        _customFolderPath = folder.CustomPath;
        _folderEditedByUser = folder.Edited;
        _colorHex = string.IsNullOrWhiteSpace(existing?.ColorHex) ? "#8A1C2B" : existing!.ColorHex;
        _selectedGradeScale = OrganizerChoices.GradeScaleKinds
            .FirstOrDefault(s => s.Value == (existing?.GradeScaleKind ?? GradeScaleKind.FivePoint))!;
        _customMaxText = existing?.GradeScaleMax is { } max ? Format(max) : "100";
        _customPassText = existing?.GradeScalePassThreshold is { } pass ? Format(pass) : "60";

        Semesters =
        [
            new NamedChoice<Guid?>(null, "— без семестра —"),
            .. semesters.Select(x => new NamedChoice<Guid?>(x.Id, $"{x.Name} · {x.CourseNumber} курс")),
        ];
        var semesterId = existing is not null ? existing.SemesterId : defaultSemesterId;
        _selectedSemester = Semesters.FirstOrDefault(x => x.Value == semesterId) ?? Semesters[0];

        ReportTemplates =
        [
            new NamedChoice<Guid?>(null, "— заводской ГОСТ 7.32-2017 —"),
            .. reportTemplates.Select(t => new NamedChoice<Guid?>(t.Id, t.Name)),
        ];
        _selectedReportTemplate = ReportTemplates
            .FirstOrDefault(t => t.Value == existing?.ReportTemplateId) ?? ReportTemplates[0];

        _selectedForecastStrategy = OrganizerChoices.ForecastStrategies
            .FirstOrDefault(s => s.Value == (existing?.ForecastStrategyName ?? string.Empty))
            ?? OrganizerChoices.ForecastStrategies[0];
    }

    public bool IsEditMode { get; }

    public IReadOnlyList<NamedChoice<SubjectAssessment>> Assessments { get; } =
        Enum.GetValues<SubjectAssessment>().Select(x => new NamedChoice<SubjectAssessment>(x, x.DisplayName())).ToArray();

    [ObservableProperty]
    private NamedChoice<SubjectAssessment> _selectedAssessment;

    public string HeaderText => IsEditMode ? "Изменить предмет" : "Новый предмет";

    public IReadOnlyList<NamedChoice<GradeScaleKind>> GradeScales => OrganizerChoices.GradeScaleKinds;

    /// <summary>Профили оформления отчётов: первый пункт — заводской (null).</summary>
    /// <summary>Семестры для пикера; первый пункт — «без семестра».</summary>
    public IReadOnlyList<NamedChoice<Guid?>> Semesters { get; }

    public IReadOnlyList<NamedChoice<Guid?>> ReportTemplates { get; }

    /// <summary>Стратегии прогноза итоговой оценки: первый пункт — по умолчанию (пустой ключ).</summary>
    public IReadOnlyList<NamedChoice<string>> ForecastStrategies => OrganizerChoices.ForecastStrategies;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name;

    [ObservableProperty]
    private string _code;

    /// <summary>Преподаватель по умолчанию (new_addons.md §5.2) — подставляется и блокируется в форме
    /// пары; пусто трактуется как <see langword="null"/> в <see cref="ToModel"/>.</summary>
    [ObservableProperty]
    private string _teacherFullName;

    [ObservableProperty]
    private NamedChoice<Guid?> _selectedSemester;

    /// <summary>
    /// Имя подпапки предмета внутри учебной папки. Пока пользователь его не трогал, следует за
    /// названием предмета (идиома <c>_outputPathEditedByUser</c> из «Нового отчёта»).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderPreviewText))]
    private string _folderName = string.Empty;

    /// <summary>Полный путь «своей папки» — задан, только если пользователь выбрал её явно.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesCustomPath))]
    [NotifyPropertyChangedFor(nameof(FolderPreviewText))]
    private string _customFolderPath = string.Empty;

    [ObservableProperty]
    private string _colorHex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(IsCustomScale))]
    private NamedChoice<GradeScaleKind> _selectedGradeScale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _customMaxText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _customPassText;

    [ObservableProperty]
    private NamedChoice<Guid?> _selectedReportTemplate;

    [ObservableProperty]
    private NamedChoice<string> _selectedForecastStrategy;

    /// <summary>Предмет живёт в своей папке вне учебной — путь показываем как есть и не трогаем.</summary>
    public bool UsesCustomPath => !string.IsNullOrWhiteSpace(CustomFolderPath);

    /// <summary>Куда в итоге ляжет папка предмета — подсказка под полем.</summary>
    public string FolderPreviewText
    {
        get
        {
            if (UsesCustomPath)
            {
                return CustomFolderPath;
            }

            var root = _workspace?.StudyRootPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root))
            {
                return "Учебная папка не выбрана — задайте её в настройках, и путь подставится сам.";
            }

            var folder = string.IsNullOrWhiteSpace(FolderName) ? Name : FolderName;
            return Path.Combine(root, SubjectFolder.Sanitize(folder));
        }
    }

    /// <summary>Произвольная шкала — показываем поля «максимум» и «порог сдачи».</summary>
    public bool IsCustomScale => SelectedGradeScale.Value == GradeScaleKind.Custom;

    /// <summary>Название непусто; для произвольной шкалы — корректные максимум и порог.</summary>
    public bool CanSave
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return false;
            }

            if (!IsCustomScale)
            {
                return true;
            }

            return TryParse(CustomMaxText, out var max) && max > 0m
                && TryParse(CustomPassText, out var pass) && pass >= 0m && pass <= max;
        }
    }

    partial void OnNameChanged(string value)
    {
        // Имя папки по умолчанию повторяет название предмета — ровно то, о чём просил тестировщик.
        if (_folderEditedByUser || IsEditMode)
        {
            return;
        }

        _syncingFolderName = true;
        FolderName = SubjectFolder.Sanitize(value);
        _syncingFolderName = false;
    }

    partial void OnFolderNameChanged(string value)
    {
        if (!_syncingFolderName)
        {
            _folderEditedByUser = true;
        }
    }

    /// <summary>Выбрать произвольную папку вне учебной — запасной путь для нестандартных случаев.</summary>
    [RelayCommand]
    private void PickCustomFolder()
    {
        if (_dialogs?.PickFolder("Папка предмета", UsesCustomPath ? CustomFolderPath : _workspace?.StudyRootPath) is { } picked)
        {
            CustomFolderPath = picked;
        }
    }

    /// <summary>
    /// Вернуть предмет в учебную папку. Файлы на диске не двигаются — меняется только настройка, о чём
    /// форма честно предупреждает: перенос содержимого пользователь делает сам, осознанно (§14).
    /// </summary>
    [RelayCommand]
    private void UseStudyFolder()
    {
        CustomFolderPath = string.Empty;
        if (string.IsNullOrWhiteSpace(FolderName))
        {
            FolderName = SubjectFolder.Sanitize(Name);
        }
    }

    /// <summary>Собрать доменную модель из полей формы.</summary>
    public Subject ToModel()
    {
        var kind = SelectedGradeScale.Value;
        decimal? customMax = null;
        decimal? customPass = null;
        if (kind == GradeScaleKind.Custom)
        {
            customMax = TryParse(CustomMaxText, out var max) ? max : null;
            customPass = TryParse(CustomPassText, out var pass) ? pass : null;
        }

        return new Subject
        {
            Id = _id,
            Name = Name.Trim(),
            Code = Code?.Trim() ?? string.Empty,
            Assessment = SelectedAssessment.Value,
            TeacherFullName = string.IsNullOrWhiteSpace(TeacherFullName) ? null : TeacherFullName.Trim(),
            SemesterId = SelectedSemester.Value,
            FolderPath = UsesCustomPath
                ? CustomFolderPath.Trim()
                : SubjectFolder.Sanitize(string.IsNullOrWhiteSpace(FolderName) ? Name : FolderName),
            ColorHex = ColorHex?.Trim() ?? string.Empty,
            GradeScaleKind = kind,
            GradeScaleMax = customMax,
            GradeScalePassThreshold = customPass,
            ForecastStrategyName = SelectedForecastStrategy.Value,
            ReportTemplateId = SelectedReportTemplate.Value,
        };
    }

    /// <summary>
    /// Разбирает <see cref="Subject.FolderPath"/> в поля формы (new_addons.md §4). Абсолютный путь
    /// <b>внутри</b> учебной папки показывается коротким именем и при сохранении свернётся в него —
    /// на диске при этом ничего не меняется. Путь <b>вне</b> учебной папки остаётся «своей папкой».
    /// </summary>
    private static (string Name, string CustomPath, bool Edited) ParseFolder(
        Subject? existing,
        IStudyWorkspace? workspace)
    {
        var stored = existing?.FolderPath?.Trim() ?? string.Empty;

        if (stored.Length == 0)
        {
            return (SubjectFolder.Sanitize(existing?.Name ?? string.Empty), string.Empty, false);
        }

        if (!SubjectFolder.IsCustomPath(stored))
        {
            return (stored, string.Empty, true);
        }

        // Путь внутри учебной папки и ровно на один уровень вглубь — это и есть «имя подпапки»,
        // просто записанное по-старому. Показываем именем, сворачиваем при сохранении.
        if (workspace?.ResolveRelative(stored) is { Length: > 0 } insideRoot
            && !insideRoot.Contains(Path.DirectorySeparatorChar)
            && !insideRoot.Contains(Path.AltDirectorySeparatorChar))
        {
            return (insideRoot, string.Empty, true);
        }

        return (SubjectFolder.FolderName(existing!), stored, true);
    }

    private static string Format(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out decimal value)
    {
        text = text?.Trim().Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
