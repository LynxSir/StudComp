using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.ReportForge.Services;
using StudComp.Services;

namespace StudComp.ViewModels.ReportForge;

/// <summary>
/// Вкладка «Новый отчёт»: markdown (набранный или импортированный), поля титульного листа,
/// путь вывода и генерация через <see cref="IReportPipeline"/> (ARCHITECTURE §10.3).
/// </summary>
public sealed partial class NewReportViewModel : ObservableObject
{
    /// <summary>Фильтр диалога импорта — Markdown и обычный текст.</summary>
    private const string MarkdownFilter = "Markdown (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|Все файлы (*.*)|*.*";

    private const string DocxFilter = "Документ Word (*.docx)|*.docx";

    private readonly IReportPipeline _pipeline;
    private readonly IReportTemplateService _templates;
    private readonly IMarkdownDocumentModelBuilder _builder;
    private readonly ISubjectService _subjects;
    private readonly INoteService _notes;
    private readonly ICardService _cards;
    private readonly ICardDeckService _cardDecks;
    private readonly IStudyWorkspace _workspace;
    private readonly IOptionsMonitor<UserProfileSettings> _profile;
    private readonly IOptionsMonitor<ReportForgeOptions> _options;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IShellLauncher _shell;
    private readonly StudComp.Data.Repositories.IActivityRepository _activity;

    /// <summary>Пользователь сам правил путь — не перетирать его при смене предмета или типа работы.</summary>
    private bool _outputPathEditedByUser;

    /// <summary>Взведён, пока путь подставляем мы сами: иначе своя же подстановка сойдёт за правку.</summary>
    private bool _updatingOutputPath;

    /// <summary>Пользователь сам выбрал шаблон — не подменять его профилем предмета до смены предмета.</summary>
    private bool _templateEditedByUser;

    /// <summary>Взведён, пока шаблон подставляем мы сами (профиль предмета или восстановление выбора).</summary>
    private bool _applyingTemplate;

    public NewReportViewModel(
        IReportPipeline pipeline,
        IReportTemplateService templates,
        IMarkdownDocumentModelBuilder builder,
        ISubjectService subjects,
        INoteService notes,
        ICardService cards,
        ICardDeckService cardDecks,
        IStudyWorkspace workspace,
        IOptionsMonitor<UserProfileSettings> profile,
        IOptionsMonitor<ReportForgeOptions> options,
        IDialogService dialogs,
        IToastService toasts,
        IShellLauncher shell,
        StudComp.Data.Repositories.IActivityRepository activity)
    {
        _pipeline = pipeline;
        _templates = templates;
        _builder = builder;
        _subjects = subjects;
        _notes = notes;
        _cards = cards;
        _cardDecks = cardDecks;
        _workspace = workspace;
        _profile = profile;
        _options = options;
        _dialogs = dialogs;
        _toasts = toasts;
        _shell = shell;
        _activity = activity;

        _workType = options.CurrentValue.DefaultWorkType;
        ApplyUserProfile();
    }

    public ObservableCollection<Subject> Subjects { get; } = [];

    public ObservableCollection<ReportTemplate> Templates { get; } = [];

    /// <summary>Структура разобранного markdown — предпросмотр того, что уедет в документ.</summary>
    public ObservableCollection<ReportBlockPreviewRowViewModel> Preview { get; } = [];

    [ObservableProperty]
    private string _markdown = string.Empty;

    /// <summary>Путь импортированного файла; пусто — текст набран прямо здесь.</summary>
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private ReportTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private bool _generateTableOfContents = true;

    [ObservableProperty]
    private bool _includeTitlePage = true;

    [ObservableProperty]
    private string _workType;

    [ObservableProperty]
    private string _university = string.Empty;

    [ObservableProperty]
    private string _faculty = string.Empty;

    [ObservableProperty]
    private string _department = string.Empty;

    [ObservableProperty]
    private string _studentName = string.Empty;

    [ObservableProperty]
    private string _studentGroup = string.Empty;

    [ObservableProperty]
    private string _supervisorName = string.Empty;

    [ObservableProperty]
    private string _city = string.Empty;

    [ObservableProperty]
    private int _year = DateTime.Today.Year;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Текст под индикатором прогресса, пока идёт генерация.</summary>
    [ObservableProperty]
    private string _generationStatusText = string.Empty;

    /// <summary>Карточка «Готово» под кнопкой «Сгенерировать» — держится, пока условия не изменились.</summary>
    [ObservableProperty]
    private bool _showGeneratedResult;

    [ObservableProperty]
    private string _lastGeneratedPath = string.Empty;

    [ObservableProperty]
    private string _lastGeneratedFileName = string.Empty;

    /// <summary>Подсказка «профиль не заполнен» — без ФИО и вуза титульный лист выйдет пустым.</summary>
    public bool IsProfileIncomplete =>
        IncludeTitlePage && (string.IsNullOrWhiteSpace(StudentName) || string.IsNullOrWhiteSpace(University));

    /// <summary>
    /// ФИО и вуз уже заполнены (обычно — автоподстановкой из профиля в Настройках) — карточку
    /// «Титульный лист» можно свернуть по умолчанию, показывать 7 полей незачем каждый раз (Phase 13.9).
    /// </summary>
    public bool TitlePageIsComplete =>
        !string.IsNullOrWhiteSpace(StudentName) && !string.IsNullOrWhiteSpace(University);

    public bool HasSource => !string.IsNullOrWhiteSpace(Markdown);

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var previouslySelectedSubject = SelectedSubject?.Id;
        var subjects = await _subjects.GetAllAsync();
        Subjects.Clear();
        foreach (var subject in subjects)
        {
            Subjects.Add(subject);
        }

        SelectedSubject = Subjects.FirstOrDefault(subject => subject.Id == previouslySelectedSubject);

        var previouslySelectedTemplate = SelectedTemplate?.Id;
        var templates = await _templates.GetAllAsync();
        Templates.Clear();
        foreach (var template in templates)
        {
            Templates.Add(template);
        }

        _applyingTemplate = true;
        try
        {
            SelectedTemplate = Templates.FirstOrDefault(template => template.Id == previouslySelectedTemplate)
                               ?? Templates.FirstOrDefault();
        }
        finally
        {
            _applyingTemplate = false;
        }

        // Если предмет привязан к профилю оформления — подставить его (пока пользователь не выбрал другой).
        ApplySubjectTemplate();

        // Настройки могли поменяться в другом разделе — подхватываем актуальные.
        ApplyUserProfile();
        UpdateSuggestedOutputPath();
        RefreshPreview();
    }

    /// <summary>
    /// Точка входа «Сгенерировать отчёт по предмету» (new_addons.md §6, Хаб/Дашборд): перечитывает
    /// списки и выбирает предмет — дальше работает та же цепочка, что и при ручном выборе в форме
    /// (<see cref="OnSelectedSubjectChanged"/> сама подставит профиль предмета и папку вывода).
    /// </summary>
    public async Task PreselectSubjectAsync(Guid subjectId)
    {
        await RefreshAsync();
        SelectedSubject = Subjects.FirstOrDefault(subject => subject.Id == subjectId) ?? SelectedSubject;
    }

    /// <summary>
    /// «Сгенерировать заново» из «Истории»: подставляет предмет/шаблон/путь прошлого запуска и, если
    /// исходный markdown-файл ещё существует на диске, — сам текст. Текст ненайденного/непереданного
    /// файла нигде не хранился — честно оставляем поле пустым, а не подставляем выдуманное содержимое.
    /// </summary>
    public async Task PrefillFromHistoryAsync(ReportJob job)
    {
        await RefreshAsync();

        SelectedSubject = Subjects.FirstOrDefault(subject => subject.Id == job.SubjectId) ?? SelectedSubject;

        _applyingTemplate = true;
        try
        {
            SelectedTemplate = Templates.FirstOrDefault(template => template.Id == job.TemplateId) ?? SelectedTemplate;
        }
        finally
        {
            _applyingTemplate = false;
        }

        _updatingOutputPath = true;
        try
        {
            OutputPath = job.OutputPath;
        }
        finally
        {
            _updatingOutputPath = false;
        }

        _outputPathEditedByUser = true;

        if (!string.IsNullOrWhiteSpace(job.SourcePath) && File.Exists(job.SourcePath))
        {
            try
            {
                Markdown = await File.ReadAllTextAsync(job.SourcePath);
                SourcePath = job.SourcePath;
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Не удалось прочитать — падаем на общее предупреждение ниже.
            }
        }

        Markdown = string.Empty;
        SourcePath = string.Empty;
        _toasts.Show(
            "Текст отчёта не сохранён",
            "Исходный markdown недоступен — наберите текст заново или импортируйте .md-файл.",
            ToastKind.Warning);
    }

    /// <summary>Шпаргалка по разметке (new_addons.md §11.4) — та же, что в редакторе заметки.</summary>
    [RelayCommand]
    private Task ShowMarkdownHelpAsync() =>
        _dialogs.ShowInfoAsync(new MarkdownHelpViewModel(), "Разметка Markdown");

    /// <summary>«Собрать из заметок» (new_addons.md §6): выбрать предмет и склеить его заметки в редактор.</summary>
    [RelayCommand]
    private async Task ComposeFromNotesAsync()
    {
        var composer = new ComposeFromNotesViewModel(Subjects, _notes, SelectedSubject?.Id);
        if (!await _dialogs.ShowEditorAsync(composer, "Собрать из заметок", "Добавить в отчёт"))
        {
            return;
        }

        Markdown = composer.BuildMarkdown();
        SourcePath = string.Empty;

        // Диалог используется только для выбора источника заметок, а не для переопределения предмета
        // отчёта исподтишка — переключаем предмет лишь если он ещё не был выбран.
        if (SelectedSubject is null)
        {
            SelectedSubject = composer.SelectedSubject;
        }
    }

    /// <summary>
    /// «Собрать из карточек» (new_addons.md §7.5): предмет/колода → мультивыбор карточек → склейка в
    /// редактор. Для макета «Шпаргалка» дополнительно подставляется клонированный компактный профиль
    /// оформления — новых возможностей рендерера это не требует (механика Phase 9).
    /// </summary>
    [RelayCommand]
    private async Task ComposeFromCardsAsync()
    {
        var composer = new ComposeFromCardsViewModel(Subjects, _cards, _cardDecks, SelectedSubject?.Id);
        if (!await _dialogs.ShowEditorAsync(composer, "Собрать из карточек", "Добавить в отчёт"))
        {
            return;
        }

        Markdown = composer.BuildMarkdown();
        SourcePath = string.Empty;

        if (SelectedSubject is null)
        {
            SelectedSubject = composer.SelectedSubject;
        }

        if (composer.Layout == CardComposeLayout.CheatSheet)
        {
            var cheatSheet = await CheatSheetTemplateProvider.EnsureAsync(_templates);
            if (Templates.All(t => t.Id != cheatSheet.Id))
            {
                Templates.Add(cheatSheet);
            }

            SelectedTemplate = cheatSheet;
        }
    }

    /// <summary>Загрузить markdown из файла на диске.</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_dialogs.PickOpenFile("Выберите markdown-файл", MarkdownFilter) is not { } path)
        {
            return;
        }

        try
        {
            Markdown = await File.ReadAllTextAsync(path);
            SourcePath = path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Не удалось прочитать файл", exception.Message, ToastKind.Error);
        }
    }

    /// <summary>Отвязаться от импортированного файла и работать с текстом в редакторе.</summary>
    [RelayCommand]
    private void ForgetSource() => SourcePath = string.Empty;

    [RelayCommand]
    private void PickOutputPath()
    {
        if (_dialogs.PickSaveFile("Куда сохранить отчёт", DocxFilter, OutputPath) is { } path)
        {
            OutputPath = path;
        }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!HasSource)
        {
            _toasts.Show("Пустой отчёт", "Наберите текст или импортируйте markdown-файл.", ToastKind.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            UpdateSuggestedOutputPath(force: true);
        }

        if (File.Exists(OutputPath)
            && !await _dialogs.ConfirmAsync(
                "Файл уже существует",
                $"Перезаписать {Path.GetFileName(OutputPath)}?",
                "Перезаписать"))
        {
            return;
        }

        IsBusy = true;
        ShowGeneratedResult = false;
        GenerationStatusText = "Готовим документ…";
        try
        {
            var request = new ReportJobRequest(
                TemplateId: SelectedTemplate?.Id,
                SubjectId: SelectedSubject?.Id,
                MarkdownSource: Markdown,
                SourcePath: string.IsNullOrWhiteSpace(SourcePath) ? null : SourcePath,
                OutputPath: OutputPath,
                TitlePage: IncludeTitlePage ? BuildTitlePage() : null,
                GenerateTableOfContents: GenerateTableOfContents);

            var result = await _pipeline.RunAsync(request, CancellationToken.None);

            if (!result.Succeeded)
            {
                _toasts.Show("Отчёт не сгенерирован", Describe(result.Error), ToastKind.Error);
                return;
            }

            _toasts.Show("Отчёт готов", Path.GetFileName(result.OutputPath) ?? string.Empty, ToastKind.Success);
            await LogGeneratedAsync(result);

            if (result.OutputPath is { } outputPath)
            {
                LastGeneratedPath = outputPath;
                LastGeneratedFileName = Path.GetFileName(outputPath);
                ShowGeneratedResult = true;
            }

            if (_options.CurrentValue.OpenAfterGenerate && result.OutputPath is { } toOpen)
            {
                _shell.OpenFile(toOpen);
            }
        }
        finally
        {
            IsBusy = false;
            GenerationStatusText = string.Empty;
        }
    }

    [RelayCommand]
    private void OpenGeneratedResult()
    {
        if (!string.IsNullOrWhiteSpace(LastGeneratedPath))
        {
            _shell.OpenFile(LastGeneratedPath);
        }
    }

    [RelayCommand]
    private void RevealGeneratedResult()
    {
        if (!string.IsNullOrWhiteSpace(LastGeneratedPath))
        {
            _shell.RevealInExplorer(LastGeneratedPath);
        }
    }

    /// <summary>Пишет факт генерации отчёта в ленту активности — для зоны «последние отчёты» Дашборда.</summary>
    private async Task LogGeneratedAsync(ReportJobResult result)
    {
        try
        {
            await _activity.AddAsync(new StudComp.Core.Domain.ActivityEntry
            {
                Id = Guid.NewGuid(),
                Kind = StudComp.Core.Domain.ActivityKind.ReportGenerated,
                Timestamp = DateTimeOffset.UtcNow,
                SubjectId = SelectedSubject?.Id,
                Path = result.OutputPath,
                RefId = result.ReportJobId,
                Title = Path.GetFileName(result.OutputPath),
            });
        }
        catch
        {
            // Лента активности — не критична для генерации отчёта.
        }
    }

    partial void OnMarkdownChanged(string value)
    {
        OnPropertyChanged(nameof(HasSource));
        RefreshPreview();
        ShowGeneratedResult = false;
    }

    partial void OnOutputPathChanged(string value)
    {
        if (!_updatingOutputPath)
        {
            _outputPathEditedByUser = true;
        }

        ShowGeneratedResult = false;
    }

    partial void OnSelectedSubjectChanged(Subject? value)
    {
        // Новый предмет приносит свой профиль оформления — прежний ручной выбор шаблона сбрасываем.
        _templateEditedByUser = false;
        ApplySubjectTemplate();
        UpdateSuggestedOutputPath();
        ShowGeneratedResult = false;
    }

    partial void OnSelectedTemplateChanged(ReportTemplate? value)
    {
        if (!_applyingTemplate)
        {
            _templateEditedByUser = true;
        }
    }

    /// <summary>Подставляет шаблон, привязанный к выбранному предмету, если пользователь не выбрал свой.</summary>
    private void ApplySubjectTemplate()
    {
        if (_templateEditedByUser || SelectedSubject?.ReportTemplateId is not { } templateId)
        {
            return;
        }

        var bound = Templates.FirstOrDefault(template => template.Id == templateId);
        if (bound is null || ReferenceEquals(bound, SelectedTemplate))
        {
            return;
        }

        _applyingTemplate = true;
        try
        {
            SelectedTemplate = bound;
        }
        finally
        {
            _applyingTemplate = false;
        }
    }

    partial void OnWorkTypeChanged(string value) => UpdateSuggestedOutputPath();

    partial void OnIncludeTitlePageChanged(bool value) => OnPropertyChanged(nameof(IsProfileIncomplete));

    partial void OnStudentNameChanged(string value) => OnPropertyChanged(nameof(IsProfileIncomplete));

    partial void OnUniversityChanged(string value) => OnPropertyChanged(nameof(IsProfileIncomplete));

    private void ApplyUserProfile()
    {
        var profile = _profile.CurrentValue;

        // Поля титульного листа — из профиля пользователя, но правятся руками под конкретный отчёт.
        University = profile.University ?? string.Empty;
        Faculty = profile.Faculty ?? string.Empty;
        Department = profile.Department ?? string.Empty;
        StudentName = profile.FullName ?? string.Empty;
        StudentGroup = profile.Group ?? string.Empty;
        City = profile.City ?? string.Empty;
    }

    private TitlePageInfo BuildTitlePage() => new(
        University: University,
        Faculty: NullIfBlank(Faculty),
        Department: NullIfBlank(Department),
        WorkType: WorkType,
        SubjectName: SelectedSubject?.Name,
        StudentName: StudentName,
        StudentGroup: NullIfBlank(StudentGroup),
        SupervisorName: NullIfBlank(SupervisorName),
        City: NullIfBlank(City),
        Year: Year);

    private void RefreshPreview()
    {
        Preview.Clear();

        if (!HasSource)
        {
            return;
        }

        foreach (var block in _builder.Build(Markdown).Blocks)
        {
            Preview.Add(new ReportBlockPreviewRowViewModel(block));
        }
    }

    /// <summary>
    /// Предлагает путь вывода: подпапка «Отчёты» предмета в учебной папке (если предмет выбран и
    /// учебная папка задана), иначе — папка из настроек, плюс имя из предмета, типа работы и даты.
    /// Правку пользователя не трогает — она приоритетнее (кроме явного <paramref name="force"/>).
    /// Директорию создавать не нужно — это делает <c>ReportPipeline</c> перед записью файла.
    /// </summary>
    private void UpdateSuggestedOutputPath(bool force = false)
    {
        if (_outputPathEditedByUser && !force)
        {
            return;
        }

        var folder = SelectedSubject is { } subject && _workspace.HasStudyRoot
            ? Path.Combine(_workspace.GetSubjectDirectory(subject), "Отчёты")
            : _options.CurrentValue.OutputFolder is { Length: > 0 } configured
                ? configured
                : RubricaPaths.ReportsDirectory;

        var parts = new[] { SelectedSubject?.Name, WorkType }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => Sanitize(part!));

        var name = string.Join(" — ", parts);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Отчёт";
        }

        _updatingOutputPath = true;
        try
        {
            OutputPath = Path.Combine(folder, $"{name} {DateTime.Today:yyyy-MM-dd}.docx");
        }
        finally
        {
            _updatingOutputPath = false;
        }
    }

    /// <summary>Выбрасывает из имени файла символы, недопустимые в Windows.</summary>
    private static string Sanitize(string value) =>
        InvalidFileNameCharacters().Replace(value, string.Empty).Trim();

    [GeneratedRegex(@"[\\/:*?""<>|]")]
    private static partial Regex InvalidFileNameCharacters();

    private static string Describe(Error? error) =>
        error is { } value && !string.IsNullOrWhiteSpace(value.Message)
            ? value.Message
            : "Неизвестная ошибка.";

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
