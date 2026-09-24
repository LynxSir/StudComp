using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.Modules.Archivist.Services;

namespace StudComp.ViewModels.Archivist;

/// <summary>
/// Форма создания/редактирования правила сортировки (диалог). Валидирует regex на лету и показывает
/// live-preview: какие файлы из наблюдаемой папки попадут под правило и во что превратятся имена
/// (ARCHITECTURE §8.7).
/// </summary>
public sealed partial class RuleEditorViewModel : ObservableObject
{
    /// <summary>Псевдо-предмет «любой» — правило без привязки кладёт файл в корень архива.</summary>
    private static readonly Subject AnySubject = new() { Id = Guid.Empty, Name = "Без предмета" };

    /// <summary>Пункт «во всех папках» — правило без привязки к конкретной наблюдаемой папке.</summary>
    private const string AnyFolder = "— во всех папках —";

    private readonly Guid _id;
    private readonly IRulePreviewService _preview;

    private CancellationTokenSource? _previewCts;

    public RuleEditorViewModel(
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<string> watchedFolders,
        ArchivistRule? existing,
        IRulePreviewService preview)
    {
        _preview = preview;
        Subjects = [AnySubject, .. subjects];
        _id = existing?.Id ?? Guid.Empty;
        IsEditMode = existing is not null;

        // Папка правила, импортированного с другого ПК, может не значиться среди наблюдаемых —
        // показываем её в списке, чтобы пользователь увидел расхождение, а не потерял значение молча.
        var folders = new List<string> { AnyFolder };
        folders.AddRange(watchedFolders);
        if (existing?.WatchedFolder is { Length: > 0 } own
            && !folders.Contains(own, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(own);
        }

        WatchedFolders = folders;
        _selectedWatchedFolder = existing?.WatchedFolder is { Length: > 0 } current
            ? folders.FirstOrDefault(f => string.Equals(f, current, StringComparison.OrdinalIgnoreCase)) ?? AnyFolder
            : AnyFolder;

        _selectedSubject = Subjects.FirstOrDefault(s => s.Id == existing?.SubjectId) ?? AnySubject;
        _selectedMatchType = ArchivistChoices.MatchTypes
            .FirstOrDefault(t => t.Value == (existing?.MatchType ?? RuleMatchType.Extension))
            ?? ArchivistChoices.MatchTypes[0];
        _pattern = existing?.Pattern ?? string.Empty;
        _workType = existing?.WorkType ?? string.Empty;
        _renameTemplate = existing?.RenameTemplate ?? string.Empty;
        _priority = existing?.Priority ?? 10;
        _enabled = existing?.Enabled ?? true;

        // Начальное состояние сворачивания «Переименовать файл при сортировке» — только на момент
        // открытия формы (уже настроенное правило разворачивается сразу, новое — свёрнуто по
        // умолчанию). Дальше пользователь управляет CardExpander сам, поле не пересчитывается.
        _hasRenameTemplate = !string.IsNullOrWhiteSpace(_renameTemplate);

        SchedulePreview();
    }

    public bool IsEditMode { get; }

    public string HeaderText => IsEditMode ? "Изменить правило" : "Новое правило";

    public IReadOnlyList<Subject> Subjects { get; }

    /// <summary>Наблюдаемые папки плюс пункт «во всех папках» первым.</summary>
    public IReadOnlyList<string> WatchedFolders { get; }

    public IReadOnlyList<NamedChoice<RuleMatchType>> MatchTypes => ArchivistChoices.MatchTypes;

    public ObservableCollection<RulePreviewRowViewModel> PreviewItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(PatternHint))]
    [NotifyPropertyChangedFor(nameof(PatternError))]
    [NotifyPropertyChangedFor(nameof(IsRegexSelected))]
    private NamedChoice<RuleMatchType> _selectedMatchType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(PatternError))]
    private string _pattern;

    [ObservableProperty]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private string _selectedWatchedFolder;

    [ObservableProperty]
    private string _workType;

    [ObservableProperty]
    private string _renameTemplate;

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private string _previewSummary = string.Empty;

    /// <summary>
    /// Начальное состояние CardExpander «Переименовать файл при сортировке» — только при открытии
    /// формы; дальше пользователь разворачивает/сворачивает сам (Phase 13.9).
    /// </summary>
    [ObservableProperty]
    private bool _hasRenameTemplate;

    /// <summary>Хотя бы один файл предпросмотра перехватывается правилом с большим приоритетом.</summary>
    [ObservableProperty]
    private bool _hasShadowedPreviewItems;

    /// <summary>Условие — регулярное выражение (для видимости пояснения о regex).</summary>
    public bool IsRegexSelected => SelectedMatchType.Value == RuleMatchType.Regex;

    /// <summary>Подсказка под полем условия — меняется вместе с типом правила.</summary>
    public string PatternHint => SelectedMatchType.Value switch
    {
        RuleMatchType.Extension => "Например: .docx",
        RuleMatchType.Regex => @"Например: ^ЛР\d+_.*\.docx$",
        _ => "Например: лекция",
    };

    /// <summary>Текст ошибки условия (кривой regex) либо <see langword="null"/>, если всё в порядке.</summary>
    public string? PatternError
    {
        get
        {
            if (SelectedMatchType.Value != RuleMatchType.Regex || string.IsNullOrWhiteSpace(Pattern))
            {
                return null;
            }

            return RegexRulePattern.IsValid(Pattern)
                ? null
                : "Регулярное выражение написано с ошибкой.";
        }
    }

    public bool HasPatternError => PatternError is not null;

    /// <summary>Форма валидна: условие заполнено и (для regex) компилируется.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Pattern) && PatternError is null;

    /// <summary>Собрать доменную модель. Вызывать только когда <see cref="CanSave"/> истинно.</summary>
    public ArchivistRule ToModel() => new()
    {
        Id = _id,
        SubjectId = SelectedSubject is null || SelectedSubject.Id == Guid.Empty ? null : SelectedSubject.Id,
        Pattern = Pattern.Trim(),
        MatchType = SelectedMatchType.Value,
        Priority = Priority,
        Enabled = Enabled,
        WorkType = string.IsNullOrWhiteSpace(WorkType) ? null : WorkType.Trim(),
        WatchedFolder = SelectedWatchedFolder == AnyFolder ? null : SelectedWatchedFolder,
        RenameTemplate = RenameTemplate?.Trim() ?? string.Empty,
    };

    partial void OnPatternChanged(string value)
    {
        OnPropertyChanged(nameof(HasPatternError));
        SchedulePreview();
    }

    partial void OnSelectedMatchTypeChanged(NamedChoice<RuleMatchType> value)
    {
        OnPropertyChanged(nameof(HasPatternError));
        SchedulePreview();
    }

    partial void OnSelectedSubjectChanged(Subject? value) => SchedulePreview();

    partial void OnSelectedWatchedFolderChanged(string value) => SchedulePreview();

    partial void OnWorkTypeChanged(string value) => SchedulePreview();

    partial void OnRenameTemplateChanged(string value) => SchedulePreview();

    private void SchedulePreview()
    {
        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        _ = RunPreviewAsync(cts.Token);
    }

    private async Task RunPreviewAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct).ConfigureAwait(true);

            if (!CanSave)
            {
                PreviewItems.Clear();
                HasShadowedPreviewItems = false;
                PreviewSummary = string.IsNullOrWhiteSpace(Pattern)
                    ? "Заполните условие, чтобы увидеть, какие файлы попадут под правило."
                    : "Исправьте условие, чтобы увидеть предпросмотр.";
                IsPreviewLoading = false;
                return;
            }

            IsPreviewLoading = true;
            var items = await _preview.PreviewAsync(ToModel(), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            PreviewItems.Clear();
            foreach (var item in items.Take(50))
            {
                PreviewItems.Add(new RulePreviewRowViewModel(item));
            }

            HasShadowedPreviewItems = items.Any(i => i.AlreadyHandledByOtherRule);
            PreviewSummary = Describe(items.Count, items.Count(i => i.AlreadyHandledByOtherRule));
        }
        catch (OperationCanceledException)
        {
            // заменён более свежим прогоном
        }
        catch (Exception)
        {
            PreviewSummary = "Не удалось построить предпросмотр.";
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsPreviewLoading = false;
            }
        }
    }

    private static string Describe(int total, int shadowed)
    {
        if (total == 0)
        {
            return "Сейчас под правило не попадает ни один файл в наблюдаемой папке.";
        }

        var text = $"Под правило попадёт файлов: {total}.";
        if (shadowed > 0)
        {
            text += $" Из них {shadowed} раньше заберёт правило с бОльшим приоритетом.";
        }

        return text;
    }
}

/// <summary>Строка списка предпросмотра правила.</summary>
public sealed class RulePreviewRowViewModel
{
    public RulePreviewRowViewModel(RulePreviewItem item)
    {
        FileName = item.FileName;
        ProjectedName = item.ProjectedName == item.FileName ? "имя не меняется" : $"→ {item.ProjectedName}";
        Target = item.ProjectedTargetDirectory;
        IsShadowed = item.AlreadyHandledByOtherRule;
    }

    public string FileName { get; }

    public string ProjectedName { get; }

    public string Target { get; }

    public bool IsShadowed { get; }
}
