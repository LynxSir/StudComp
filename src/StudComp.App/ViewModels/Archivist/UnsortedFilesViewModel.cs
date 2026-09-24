using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using StudComp.Behaviors;
using StudComp.Controls;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Archivist.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels.Shell;

namespace StudComp.ViewModels.Archivist;

/// <summary>
/// Вкладка «Неразобранное» (ARCHITECTURE §8.4 п.7, редизайн Phase 12.2 — new_addons.md §4): подсказки
/// предметов-кандидатов, инлайн-выбор предмета в строке, пакетная сортировка, drag-and-drop на чип
/// предмета. Ручная сортировка одиночного файла — выбрать предмет (или кликнуть по чипу-подсказке) и
/// нажать «Разобрать».
/// </summary>
public sealed partial class UnsortedFilesViewModel : ObservableObject, IDisposable
{
    private const int SortedTodayLookback = 500;

    private readonly IUnsortedFileService _unsorted;
    private readonly ISubjectService _subjects;
    private readonly IArchivistRuleService _rules;
    private readonly IUnsortedSuggestionService _suggestions;
    private readonly IFileWatcherService _watcher;
    private readonly IToastService _toasts;
    private readonly IActivityRepository _activity;
    private readonly IMessenger _messenger;

    /// <summary>Id файлов, для которых уже идёт сортировка — защита от двойного клика/drop (не риск
    /// потери данных, а риск задвоенного UI-действия: <see cref="IUnsortedFileService.SortManuallyAsync"/>
    /// сам по себе идемпотентен).</summary>
    private readonly HashSet<Guid> _inFlight = [];

    public UnsortedFilesViewModel(
        IUnsortedFileService unsorted,
        ISubjectService subjects,
        IArchivistRuleService rules,
        IUnsortedSuggestionService suggestions,
        IFileWatcherService watcher,
        IToastService toasts,
        IActivityRepository activity,
        IMessenger messenger,
        FilePreviewViewModel preview)
    {
        _unsorted = unsorted;
        _subjects = subjects;
        _rules = rules;
        _suggestions = suggestions;
        _watcher = watcher;
        _toasts = toasts;
        _activity = activity;
        _messenger = messenger;
        Preview = preview;

        _watcher.StateChanged += OnWatcherStateChanged;
    }

    /// <summary>Панель предпросмотра выбранного файла (§1.10) — следует за <see cref="FocusedRow"/>.</summary>
    public FilePreviewViewModel Preview { get; }

    public ObservableCollection<UnsortedFileRowViewModel> Items { get; } = [];

    public ObservableCollection<Subject> Subjects { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Наблюдение выключено или папка не выбрана — показываем подсказку вместо пустого списка.</summary>
    [ObservableProperty]
    private bool _isWatchingDisabled;

    [ObservableProperty]
    private string _watchedFolderText = string.Empty;

    /// <summary>Сколько файлов уже разложено сегодня — счётчик над списком (new_addons.md §4).</summary>
    [ObservableProperty]
    private int _sortedTodayCount;

    /// <summary>Предмет для панели пакетной сортировки — независим от инлайн-выбора в строках.</summary>
    [ObservableProperty]
    private Subject? _batchTargetSubject;

    /// <summary>Строка под курсором/выделением в списке — по ней строится <see cref="Preview"/>.</summary>
    [ObservableProperty]
    private UnsortedFileRowViewModel? _focusedRow;

    partial void OnFocusedRowChanged(UnsortedFileRowViewModel? value) => Preview.Load(value?.Path);

    public bool HasSelection => Items.Any(i => i.IsSelected);

    public int SelectedCount => Items.Count(i => i.IsSelected);

    /// <summary>Список пуст и не идёт загрузка — дружелюбная заглушка вместо голого пустого списка.</summary>
    public bool IsEmpty => !IsBusy && Items.Count == 0;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            IsWatchingDisabled = !_watcher.IsWatching;
            WatchedFolderText = DescribeFolders(_watcher.WatchedFolders);

            var subjectList = await _subjects.GetAllAsync();
            Subjects.Clear();
            foreach (var subject in subjectList)
            {
                Subjects.Add(subject);
            }

            var ruleList = await _rules.GetAllAsync();
            var records = await _unsorted.GetUnsortedAsync();

            // Размер каждого файла — единственное блокирующее I/O в построении списка; считаем пачкой
            // одним проходом ожидания, а не синхронно на каждую строку (DoD: 1000+ файлов без фризов).
            var sizes = await Task.WhenAll(records.Select(r => Task.Run(() => SafeSize(PathOf(r)))));

            foreach (var row in Items)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }

            Items.Clear();
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var suggestions = _suggestions.Suggest(System.IO.Path.GetFileName(PathOf(record)), subjectList, ruleList);
                var row = new UnsortedFileRowViewModel(record, sizes[i], suggestions, subjectList);
                row.PropertyChanged += OnRowPropertyChanged;
                Items.Add(row);
            }

            SortedTodayCount = await CountSortedTodayAsync();

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(IsEmpty));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        _watcher.RequestRescan();
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenArchivistSettings() => _messenger.Send(new OpenSettingsMessage(SettingsSection.Archivist));

    [RelayCommand]
    private async Task SortAsync(UnsortedFileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.SelectedSubject is null)
        {
            if (Subjects.Count == 0)
            {
                _toasts.Show("Нет предметов", "Сначала добавьте предмет в разделе «Органайзер».", ToastKind.Warning);
            }
            else
            {
                _toasts.Show("Выберите предмет", "Укажите предмет в строке или кликните по подсказке.", ToastKind.Warning);
            }

            return;
        }

        await SortManyAsync([row], row.SelectedSubject);
    }

    [RelayCommand]
    private async Task BatchSortAsync()
    {
        var rows = Items.Where(i => i.IsSelected).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        if (BatchTargetSubject is null)
        {
            _toasts.Show("Выберите предмет", "Укажите, в какой предмет разложить выбранные файлы.", ToastKind.Warning);
            return;
        }

        await SortManyAsync(rows, BatchTargetSubject);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Items)
        {
            row.IsSelected = false;
        }
    }

    /// <summary>Точка входа для drag-and-drop на чип предмета (<see cref="DragDropSortBehavior"/>).</summary>
    [RelayCommand]
    private async Task DropSortAsync(DragDropSortBehavior.DropPayload? payload)
    {
        if (payload is null || payload.Rows.Count == 0)
        {
            return;
        }

        await SortManyAsync(payload.Rows, payload.Subject);
    }

    [RelayCommand]
    private async Task ForgetAsync(UnsortedFileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var result = await _unsorted.ForgetAsync(row.Record.Id);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось убрать из списка", result.Error.Message, ToastKind.Error);
        }

        await RefreshAsync();
    }

    private async Task SortManyAsync(IReadOnlyList<UnsortedFileRowViewModel> rows, Subject subject)
    {
        var pending = rows.Where(r => _inFlight.Add(r.Record.Id)).ToList();
        if (pending.Count == 0)
        {
            // Всё уже в процессе сортировки — двойной клик/drop молча игнорируется.
            return;
        }

        try
        {
            var outcomes = new List<(UnsortedFileRowViewModel Row, Result Result)>();
            foreach (var row in pending)
            {
                outcomes.Add((row, await _unsorted.SortManuallyAsync(row.Record.Id, subject.Id)));
            }

            var failedCount = outcomes.Count(o => o.Result.IsFailure);

            if (outcomes.Count == 1)
            {
                var (row, result) = outcomes[0];
                if (result.IsFailure)
                {
                    _toasts.Show("Не удалось разложить файл", result.Error.Message, ToastKind.Error);
                }
                else
                {
                    _toasts.Show("Файл разложен", $"{row.FileName} перемещён в «{subject.Name}»", ToastKind.Success);
                }
            }
            else
            {
                var succeeded = outcomes.Count - failedCount;
                var kind = failedCount > 0 ? ToastKind.Warning : ToastKind.Success;
                var text = $"Разложено {succeeded} из {outcomes.Count} в «{subject.Name}»."
                    + (failedCount > 0 ? $" Не удалось: {failedCount}." : string.Empty);
                _toasts.Show("Пакетная сортировка", text, kind);
            }
        }
        finally
        {
            foreach (var row in pending)
            {
                _inFlight.Remove(row.Record.Id);
            }
        }

        await RefreshAsync();
    }

    private async Task<int> CountSortedTodayAsync()
    {
        var entries = await _activity.GetRecentByKindsAsync(SortedTodayLookback, [ActivityKind.FileSorted]);
        var today = DateTimeOffset.Now.Date;
        return entries.Count(e => e.Timestamp.LocalDateTime.Date == today);
    }

    private static string PathOf(FileRecord record) => record.CurrentPath.Length > 0 ? record.CurrentPath : record.OriginalPath;

    private static long SafeSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Одна папка — показываем путь целиком, несколько — счётчик: полный список в шапке не помещается,
    /// а увидеть его можно в Настройках.
    /// </summary>
    private static string DescribeFolders(IReadOnlyList<string> folders) => folders.Count switch
    {
        0 => "папка не выбрана",
        1 => folders[0],
        _ => $"папок под наблюдением: {folders.Count}",
    };

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnsortedFileRowViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    public void Dispose()
    {
        _watcher.StateChanged -= OnWatcherStateChanged;
        foreach (var row in Items)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Preview.Dispose();
    }

    /// <summary>
    /// Событие приходит из фонового воркера — на UI-поток возвращаемся явно через диспетчер
    /// (ARCHITECTURE §11.4).
    /// </summary>
    private void OnWatcherStateChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.InvokeAsync(() => _ = RefreshAsync());
}
