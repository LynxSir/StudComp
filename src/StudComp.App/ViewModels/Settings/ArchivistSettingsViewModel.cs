using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;
using StudComp.Services;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Раздел «Архивариус» (new_addons.md §7 §4): наблюдение, папки, игнор-паттерны, пороги,
/// продвинутые параметры. Сервисы Архивариуса не меняются — только их настройки.
/// </summary>
public sealed partial class ArchivistSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly UserSettingsProvider _settings;
    private readonly IDialogService _dialogs;
    private readonly IOptions<WorkspaceOptions> _workspace;

    public ArchivistSettingsViewModel(
        UserSettingsProvider settings,
        IDialogService dialogs,
        IOptions<ArchivistOptions> archivist,
        IOptions<WorkspaceOptions> workspace)
    {
        _settings = settings;
        _dialogs = dialogs;
        _workspace = workspace;

        var o = archivist.Value;
        using (BeginLoad())
        {
            _enabled = o.Enabled;
            _watchStudyRoot = o.WatchStudyRoot;
            _archiveRootFolder = o.ArchiveRootFolder;
            _minFileSizeKib = Math.Max(0, o.MinFileSizeBytes / 1024);
            _minFileAgeSeconds = o.MinFileAgeSeconds;
            _regexMatchTimeoutMs = o.RegexMatchTimeoutMs;
            _deferredRetryMaxAttempts = o.DeferredRetryMaxAttempts;
            _deferredRetryInitialSeconds = o.DeferredRetryInitialSeconds;
            _reconciliationIntervalMinutes = o.ReconciliationIntervalMinutes;
            _watcherHealthCheckSeconds = o.WatcherHealthCheckSeconds;
            _skipCloudPlaceholders = o.SkipCloudPlaceholders;
            _deadlineLinkSuggestionEnabled = o.DeadlineLinkSuggestionEnabled;

            foreach (var folder in o.WatchedFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                WatchedFolders.Add(folder);
            }

            foreach (var pattern in (o.IgnoredPatterns ?? []).Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                IgnoredPatterns.Add(pattern);
            }
        }
    }

    public override SettingsSection Section => SettingsSection.Archivist;

    public override string Title => "Архивариус";

    public override SymbolRegular Icon => SymbolRegular.FolderArrowRight24;

    public override IEnumerable<string> SearchKeywords =>
        [
            "архивариус", "наблюдение", "папки", "загрузки", "рабочий стол", "игнор", "паттерны",
            "размер файла", "возраст", "regex", "таймаут", "ретрай", "сверка", "reconciliation",
            "облачные", "onedrive", "дедлайн", "корень архива",
        ];

    /// <summary>Наблюдаемые папки.</summary>
    public ObservableCollection<string> WatchedFolders { get; } = [];

    /// <summary>Игнор-паттерны (маски имён файлов).</summary>
    public ObservableCollection<string> IgnoredPatterns { get; } = [];

    public bool HasWatchedFolders => WatchedFolders.Count > 0;

    /// <summary>Учебная папка из настроек — цель галочки «наблюдать учебную папку».</summary>
    private string StudyRootPath => _workspace.Value.StudyRootPath ?? string.Empty;

    public bool HasStudyRoot => !string.IsNullOrWhiteSpace(StudyRootPath);

    /// <summary>Путь учебной папки или подсказка, если она ещё не выбрана.</summary>
    public string StudyRootDisplay =>
        HasStudyRoot ? StudyRootPath : "Учебная папка не выбрана — задайте её в разделе «Учебная папка»";

    /// <summary>Путь корня архива или подсказка, что по умолчанию используется учебная папка.</summary>
    public string ArchiveRootFolderDisplay =>
        string.IsNullOrWhiteSpace(ArchiveRootFolder)
            ? (HasStudyRoot ? $"Учебная папка: {StudyRootPath}" : "Папка не выбрана")
            : ArchiveRootFolder;

    /// <summary>
    /// Корень архива задан и ведёт не туда, где Хаб предмета показывает файлы. Это законная, но
    /// редкая конфигурация — предупреждаем и предлагаем свести одним нажатием (new_addons.md §4).
    /// </summary>
    public bool ArchiveRootDivergesFromStudyRoot =>
        !string.IsNullOrWhiteSpace(ArchiveRootFolder)
        && HasStudyRoot
        && !string.Equals(
            ArchiveRootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StudyRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private bool _watchStudyRoot;

    [ObservableProperty]
    private string _archiveRootFolder;

    [ObservableProperty]
    private string _newIgnoredPattern = string.Empty;

    [ObservableProperty]
    private int _minFileSizeKib;

    [ObservableProperty]
    private int _minFileAgeSeconds;

    [ObservableProperty]
    private int _regexMatchTimeoutMs;

    [ObservableProperty]
    private int _deferredRetryMaxAttempts;

    [ObservableProperty]
    private int _deferredRetryInitialSeconds;

    [ObservableProperty]
    private int _reconciliationIntervalMinutes;

    [ObservableProperty]
    private int _watcherHealthCheckSeconds;

    [ObservableProperty]
    private bool _skipCloudPlaceholders;

    [ObservableProperty]
    private bool _deadlineLinkSuggestionEnabled;

    partial void OnEnabledChanged(bool value) => Persist(o => o.Enabled = value);

    partial void OnArchiveRootFolderChanged(string value)
    {
        OnPropertyChanged(nameof(ArchiveRootFolderDisplay));
        OnPropertyChanged(nameof(ArchiveRootDivergesFromStudyRoot));
        Persist(o => o.ArchiveRootFolder = value ?? string.Empty);
    }

    partial void OnMinFileSizeKibChanged(int value) => Persist(o => o.MinFileSizeBytes = Math.Max(0, value) * 1024);

    partial void OnMinFileAgeSecondsChanged(int value) => Persist(o => o.MinFileAgeSeconds = Math.Max(0, value));

    partial void OnRegexMatchTimeoutMsChanged(int value) => Persist(o => o.RegexMatchTimeoutMs = Math.Max(10, value));

    partial void OnDeferredRetryMaxAttemptsChanged(int value) => Persist(o => o.DeferredRetryMaxAttempts = Math.Max(0, value));

    partial void OnDeferredRetryInitialSecondsChanged(int value) => Persist(o => o.DeferredRetryInitialSeconds = Math.Max(1, value));

    partial void OnReconciliationIntervalMinutesChanged(int value) => Persist(o => o.ReconciliationIntervalMinutes = Math.Max(1, value));

    partial void OnWatcherHealthCheckSecondsChanged(int value) => Persist(o => o.WatcherHealthCheckSeconds = Math.Max(10, value));

    partial void OnWatchStudyRootChanged(bool value) => Persist(o => o.WatchStudyRoot = value);

    partial void OnSkipCloudPlaceholdersChanged(bool value) => Persist(o => o.SkipCloudPlaceholders = value);

    partial void OnDeadlineLinkSuggestionEnabledChanged(bool value) => Persist(o => o.DeadlineLinkSuggestionEnabled = value);

    [RelayCommand]
    private void AddWatchedFolder()
    {
        if (_dialogs.PickFolder("Папка для наблюдения", WatchedFolders.LastOrDefault()) is not { } picked)
        {
            return;
        }

        // Сравниваем нормализованные пути: иначе «C:\Загрузки» и «C:\Загрузки\» пройдут как разные
        // и в списке появится дубль, который схлопнется только внутри наблюдателя.
        if (!WatchedFolders.Any(f => SamePath(f, picked)))
        {
            WatchedFolders.Add(picked);
            PersistWatchedFolders();
        }
    }

    [RelayCommand]
    private void RemoveWatchedFolder(string? folder)
    {
        if (folder is not null && WatchedFolders.Remove(folder))
        {
            PersistWatchedFolders();
        }
    }

    [RelayCommand]
    private void PickArchiveRootFolder()
    {
        if (_dialogs.PickFolder("Корень архива", ArchiveRootFolder) is { } picked)
        {
            ArchiveRootFolder = picked;
        }
    }

    /// <summary>Убрать переопределение — цель сортировки снова считается от учебной папки.</summary>
    [RelayCommand]
    private void UseStudyRootAsArchive() => ArchiveRootFolder = string.Empty;

    [RelayCommand]
    private void AddIgnoredPattern()
    {
        var pattern = NewIgnoredPattern?.Trim();
        if (string.IsNullOrWhiteSpace(pattern) || IgnoredPatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        IgnoredPatterns.Add(pattern);
        NewIgnoredPattern = string.Empty;
        PersistIgnoredPatterns();
    }

    [RelayCommand]
    private void RemoveIgnoredPattern(string? pattern)
    {
        if (pattern is not null && IgnoredPatterns.Remove(pattern))
        {
            PersistIgnoredPatterns();
        }
    }

    private void PersistWatchedFolders()
    {
        OnPropertyChanged(nameof(HasWatchedFolders));
        if (IsLoading)
        {
            return;
        }

        var folders = WatchedFolders.ToList();
        _settings.Update<ArchivistOptions>(ArchivistOptions.SectionName, o => o.WatchedFolders = folders);
    }

    private void PersistIgnoredPatterns()
    {
        if (IsLoading)
        {
            return;
        }

        var patterns = IgnoredPatterns.ToList();
        _settings.Update<ArchivistOptions>(ArchivistOptions.SectionName, o => o.IgnoredPatterns = patterns);
    }

    /// <summary>Сравнение путей без учёта регистра и хвостового разделителя.</summary>
    private static bool SamePath(string left, string right)
    {
        static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (ArgumentException)
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private void Persist(Action<ArchivistOptions> mutate)
    {
        if (!IsLoading)
        {
            _settings.Update(ArchivistOptions.SectionName, mutate);
        }
    }
}
