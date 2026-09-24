using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Archivist.Services;
using StudComp.Modules.Organizer.Services;

namespace StudComp.ViewModels.Archivist;

/// <summary>Диапазон дат для фильтра Журнала (Phase 12.2, new_addons.md §4).</summary>
public enum LogDateFilter
{
    Today,
    Last7Days,
    Last30Days,
    All,
}

/// <summary>Исход операции для фильтра Журнала.</summary>
public enum LogOutcomeFilter
{
    All,
    Moved,
    Failed,
    RolledBack,
}

/// <summary>
/// Вкладка «Журнал» (ARCHITECTURE §8.6, редизайн Phase 12.2): последние файловые операции архивариуса
/// таймлайном, у каждой успешной — кнопка «Отменить», у неудавшихся — переход в «Неразобранное»,
/// плюс фильтры по дате/исходу/предмету поверх уже загруженного набора (контракт
/// <see cref="IOperationHistoryService"/> не меняется — фильтрация клиентская).
/// </summary>
public sealed partial class OperationsLogViewModel : ObservableObject, IDisposable
{
    /// <summary>Достаточно с запасом для клиентской фильтрации, не раздувая контракт сервиса.</summary>
    private const int Take = 300;

    private readonly IOperationHistoryService _history;
    private readonly IFileWatcherService _watcher;
    private readonly IReconciliationService _reconciliation;
    private readonly IToastService _toasts;
    private readonly ISubjectService _subjects;
    private readonly IStudyWorkspace _workspace;

    private List<OperationLogRowViewModel> _allRows = [];

    public OperationsLogViewModel(
        IOperationHistoryService history,
        IFileWatcherService watcher,
        IReconciliationService reconciliation,
        IToastService toasts,
        ISubjectService subjects,
        IStudyWorkspace workspace)
    {
        _history = history;
        _watcher = watcher;
        _reconciliation = reconciliation;
        _toasts = toasts;
        _subjects = subjects;
        _workspace = workspace;
        _watcher.StateChanged += OnWatcherStateChanged;
    }

    /// <summary>Устанавливается родительской <c>ArchivistPageViewModel</c> — переключить на «Неразобранное».</summary>
    public Action? RequestOpenUnsorted { get; set; }

    public ObservableCollection<OperationLogRowViewModel> Items { get; } = [];

    public ObservableCollection<Subject> Subjects { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private LogDateFilter _dateFilter = LogDateFilter.All;

    [ObservableProperty]
    private LogOutcomeFilter _outcomeFilter = LogOutcomeFilter.All;

    [ObservableProperty]
    private Subject? _subjectFilter;

    public bool HasActiveFilter => DateFilter != LogDateFilter.All
        || OutcomeFilter != LogOutcomeFilter.All
        || SubjectFilter is not null;

    partial void OnDateFilterChanged(LogDateFilter value) => ApplyFilters();

    partial void OnOutcomeFilterChanged(LogOutcomeFilter value) => ApplyFilters();

    partial void OnSubjectFilterChanged(Subject? value) => ApplyFilters();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var subjectList = await _subjects.GetAllAsync();
            Subjects.Clear();
            foreach (var subject in subjectList)
            {
                Subjects.Add(subject);
            }

            var rows = await _history.GetRecentAsync(Take);
            _allRows = rows.Select(r => new OperationLogRowViewModel(r, () => RequestOpenUnsorted?.Invoke())).ToList();
            ApplyFilters();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        DateFilter = LogDateFilter.All;
        OutcomeFilter = LogOutcomeFilter.All;
        SubjectFilter = null;
    }

    /// <summary>
    /// Ручной запуск сверки «диск ↔ учёт» (ARCHITECTURE §8.5). Сверка идёт в фоне и сама разбудит
    /// список через <c>StateChanged</c>, когда что-то найдёт.
    /// </summary>
    [RelayCommand]
    private void Reconcile()
    {
        _reconciliation.RequestRun("кнопка «Проверить папки» в журнале");
        _toasts.Show(
            "Проверяем папки",
            "Сверяем содержимое наблюдаемых папок с учётом — найденные файлы появятся в «Неразобранном».",
            ToastKind.Info);
    }

    [RelayCommand]
    private async Task UndoAsync(OperationLogRowViewModel? row)
    {
        if (row is null || !row.CanUndo)
        {
            return;
        }

        var result = await _history.UndoAsync(row.Item.FileRecordId);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось отменить", result.Error.Message, ToastKind.Warning);
        }
        else
        {
            _toasts.Show("Сортировка отменена", $"{row.FileName} вернулся на прежнее место.", ToastKind.Info);
        }

        await RefreshAsync();
    }

    private void ApplyFilters()
    {
        var now = DateTimeOffset.Now;
        IEnumerable<OperationLogRowViewModel> query = _allRows;

        query = DateFilter switch
        {
            LogDateFilter.Today => query.Where(r => r.Item.StartedAt.LocalDateTime.Date == now.Date),
            LogDateFilter.Last7Days => query.Where(r => r.Item.StartedAt >= now.AddDays(-7)),
            LogDateFilter.Last30Days => query.Where(r => r.Item.StartedAt >= now.AddDays(-30)),
            _ => query,
        };

        query = OutcomeFilter switch
        {
            LogOutcomeFilter.Moved => query.Where(r => r.Item.Status == FileOperationLogStatus.Completed),
            LogOutcomeFilter.Failed => query.Where(r => r.Item.Status == FileOperationLogStatus.Failed),
            LogOutcomeFilter.RolledBack => query.Where(r => r.Item.Status == FileOperationLogStatus.RolledBack),
            _ => query,
        };

        if (SubjectFilter is { } subject
            && _workspace.GetSubjectDirectory(subject) is { Length: > 0 } subjectDirectory)
        {
            // Эвристика: у OperationHistoryItem нет SubjectId, сопоставляем по каталогу назначения.
            // Путь считаем через IStudyWorkspace — с Phase 13.2 Subject.FolderPath может быть просто
            // именем подпапки, и сравнение с ним напрямую сломалось бы молча.
            // Записи без FinalPath (Failed/Planned) под фильтр предмета не попадают — ожидаемое ограничение.
            query = query.Where(r => r.Item.FinalPath is { } final
                && final.StartsWith(subjectDirectory, StringComparison.OrdinalIgnoreCase));
        }

        Items.Clear();
        foreach (var row in query)
        {
            Items.Add(row);
        }

        IsEmpty = _allRows.Count == 0;
        OnPropertyChanged(nameof(HasActiveFilter));
    }

    public void Dispose() => _watcher.StateChanged -= OnWatcherStateChanged;

    private void OnWatcherStateChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.InvokeAsync(() => _ = RefreshAsync());
}

/// <summary>Строка журнала операций. Неизменяемая — как строки Органайзера.</summary>
public sealed class OperationLogRowViewModel
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public OperationLogRowViewModel(OperationHistoryItem item, Action openUnsorted)
    {
        Item = item;
        FileName = item.FileName;
        WhenText = item.StartedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm", Russian);
        FromText = item.OriginalPath;
        ToText = item.FinalPath ?? "—";
        CanUndo = item.CanUndo;
        IsFailed = item.Status == FileOperationLogStatus.Failed;
        IsCompleted = item.Status == FileOperationLogStatus.Completed;
        IsRolledBack = item.Status == FileOperationLogStatus.RolledBack;
        OpenUnsortedCommand = new RelayCommand(openUnsorted);
        StatusText = item.Status switch
        {
            FileOperationLogStatus.Completed => CanUndo ? "перемещён" : "перемещён (файл уже унесли)",
            FileOperationLogStatus.RolledBack => "возвращён по отмене",
            FileOperationLogStatus.Failed => "не удалось переместить",
            _ => "в процессе",
        };
    }

    public OperationHistoryItem Item { get; }

    public string FileName { get; }

    public string WhenText { get; }

    public string FromText { get; }

    public string ToText { get; }

    public string StatusText { get; }

    public bool CanUndo { get; }

    /// <summary>Не удалось разложить — такой файл сейчас сидит в «Неразобранном» (Quarantined/Deferred).</summary>
    public bool IsFailed { get; }

    public bool IsCompleted { get; }

    public bool IsRolledBack { get; }

    public IRelayCommand OpenUnsortedCommand { get; }
}
