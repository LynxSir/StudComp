using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Behaviors;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.ReportForge.Services;
using StudComp.Resources;
using StudComp.Services;
using StudComp.ViewModels;

namespace StudComp.ViewModels.Organizer;

/// <summary>Метка часа в левом гаттере сетки расписания.</summary>
public sealed record HourLabel(int RowIndex, string Text);

/// <summary>Заголовок колонки-дня с признаком «сегодня».</summary>
public sealed record DayHeader(string Name, string ShortName, bool IsToday);

/// <summary>
/// Вкладка «Расписание»: сетка время×день (ARCHITECTURE §9.2, редизайн — new_addons.md §5).
/// Клик по пустой ячейке создаёт пару поповером, плитку можно перетащить и растянуть мышью,
/// одиночный клик по паре ведёт в Хаб предмета.
/// </summary>
public sealed partial class ScheduleViewModel : ObservableObject
{
    /// <summary>Начало оси времени (час). Сетка — с 08:00.</summary>
    public const int DayStartHour = 8;

    /// <summary>Конец оси времени (час). Сетка — до 21:00.</summary>
    public const int DayEndHour = 21;

    /// <summary>Шаг сетки в минутах.</summary>
    public const int SlotMinutes = 30;

    /// <summary>Высота получасовой строки, px — от неё считается высота полотна.</summary>
    public const double SlotPixelHeight = 26;

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(90);

    private readonly IScheduleService _schedule;
    private readonly ISubjectService _subjects;
    private readonly ISemesterService _semesters;
    private readonly IReportTemplateService _reportTemplates;
    private readonly IStudyWorkspace _workspace;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly INotificationScheduler _scheduler;
    private readonly INavigationService _navigation;

    private IReadOnlyList<Subject> _subjectCache = [];
    private IReadOnlyList<PairSlot> _slots = [];
    private IReadOnlyList<ScheduleEntry> _entryCache = [];
    private Semester? _activeSemester;

    /// <summary>Последний использованный день недели — внутрисессионная память вкладки, не
    /// персистентная настройка (new_addons.md §5.3): теряется при пересоздании VM по навигации.</summary>
    private DayOfWeek? _lastUsedDay;

    public ScheduleViewModel(
        IScheduleService schedule,
        ISubjectService subjects,
        ISemesterService semesters,
        IReportTemplateService reportTemplates,
        IStudyWorkspace workspace,
        IDialogService dialogs,
        IToastService toasts,
        INotificationScheduler scheduler,
        INavigationService navigation)
    {
        _schedule = schedule;
        _subjects = subjects;
        _semesters = semesters;
        _reportTemplates = reportTemplates;
        _workspace = workspace;
        _dialogs = dialogs;
        _toasts = toasts;
        _scheduler = scheduler;
        _navigation = navigation;

        Popover.Submitted += async (_, _) => await CreateFromPopoverAsync();
        Popover.NewSubjectRequested += async (_, _) => await CreateSubjectFromPopoverAsync();
    }

    /// <summary>Число строк сетки.</summary>
    public int SlotCount => (DayEndHour - DayStartHour) * 60 / SlotMinutes;

    /// <summary>Высота полотна в пикселях — выводится из числа строк, а не задаётся числом в разметке.</summary>
    public double GridPixelHeight => SlotCount * SlotPixelHeight;

    /// <summary>Метки часов для левого гаттера.</summary>
    public IReadOnlyList<HourLabel> HourLabels { get; } =
        [.. Enumerable.Range(DayStartHour, DayEndHour - DayStartHour)
            .Select(h => new HourLabel((h - DayStartHour) * 60 / SlotMinutes, $"{h}:00"))];

    /// <summary>Заголовки семи колонок-дней с признаком «сегодня».</summary>
    public IReadOnlyList<DayHeader> DayHeaders { get; } =
        [.. OrganizerChoices.Days.Select(d => new DayHeader(
            d.Display,
            d.Display[..2].ToUpperInvariant(),
            d.Value == DateTime.Today.DayOfWeek))];

    /// <summary>Варианты фильтра чётности: каждую неделю / числитель / знаменатель.</summary>
    public IReadOnlyList<NamedChoice<WeekParity>> ParityFilters => OrganizerChoices.Parities;

    public ObservableCollection<ScheduleTileViewModel> Tiles { get; } = [];

    /// <summary>Форма быстрого добавления пары по клику на пустую ячейку.</summary>
    public SchedulePopoverViewModel Popover { get; } = new();

    [ObservableProperty]
    private NamedChoice<WeekParity> _selectedParityFilter = OrganizerChoices.Parities[0];

    /// <summary>Показывать обе недели сразу — числитель и знаменатель встают рядом.</summary>
    [ObservableProperty]
    private bool _showBothWeeks;

    [ObservableProperty]
    private string _weekHeader = string.Empty;

    /// <summary>Чётность текущей недели одним словом — чип в шапке.</summary>
    [ObservableProperty]
    private string _currentParityText = string.Empty;

    [ObservableProperty]
    private bool _hasSemester;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Смещение линии «сейчас» от верха полотна, px; отрицательное — линию не рисуем.</summary>
    [ObservableProperty]
    private double _nowLineOffset = -1;

    /// <summary>Колонка сегодняшнего дня — по ней позиционируется линия «сейчас».</summary>
    [ObservableProperty]
    private int _todayColumn = -1;

    public bool ShowNowLine => NowLineOffset >= 0 && TodayColumn >= 0;

    public bool HasTiles => Tiles.Count > 0;

    partial void OnSelectedParityFilterChanged(NamedChoice<WeekParity> value) => _ = RefreshAsync();

    partial void OnShowBothWeeksChanged(bool value) => _ = RefreshAsync();

    partial void OnNowLineOffsetChanged(double value) => OnPropertyChanged(nameof(ShowNowLine));

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var semester = await _semesters.GetActiveAsync();
            _activeSemester = semester;
            SyncHeader(semester);
            _slots = PairSlots.Parse(semester?.PairSlotsJson);

            _subjectCache = await FilterBySemesterAsync(semester);
            var byId = _subjectCache.ToDictionary(s => s.Id);

            // «Обе недели» отключает фильтр: смысл режима — увидеть числитель и знаменатель рядом.
            var filter = ShowBothWeeks || SelectedParityFilter.Value == WeekParity.Any
                ? (WeekParity?)null
                : SelectedParityFilter.Value;

            var entries = (await _schedule.GetForWeekAsync(filter))
                .Where(e => byId.ContainsKey(e.SubjectId))
                .ToArray();
            _entryCache = entries;

            var lanes = ScheduleLaneLayout.Assign(entries).ToDictionary(x => x.EntryId);

            Tiles.Clear();
            foreach (var entry in entries)
            {
                var subject = byId.GetValueOrDefault(entry.SubjectId);
                var lane = lanes.GetValueOrDefault(entry.Id);

                Tiles.Add(new ScheduleTileViewModel(
                    entry,
                    subject?.Name ?? "—",
                    subject?.ColorHex,
                    DayStartHour,
                    SlotMinutes,
                    lane.Lane,
                    Math.Max(1, lane.LaneCount)));
            }

            UpdateNowMarkers(semester);
            OnPropertyChanged(nameof(HasTiles));
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- создание пары кликом по пустой ячейке ----------------------------------------------

    /// <summary>Переключить фильтр чётности из сегментированного контрола шапки.</summary>
    [RelayCommand]
    private void SetParityFilter(NamedChoice<WeekParity>? choice)
    {
        if (choice is not null)
        {
            SelectedParityFilter = choice;
        }
    }

    [RelayCommand]
    private void OpenCell(ScheduleCellHit? hit)
    {
        if (hit is null)
        {
            return;
        }

        var day = OrganizerChoices.Days[Math.Clamp(hit.DayColumn, 0, 6)].Value;
        var time = new TimeOnly(DayStartHour, 0).AddMinutes(hit.RowIndex * SlotMinutes);
        var sameDay = _entryCache.Where(e => e.DayOfWeek == day).ToArray();

        Popover.Open(day, time, _subjectCache, _slots, DefaultDuration, sameDay, _activeSemester);
    }

    private async Task CreateFromPopoverAsync()
    {
        Popover.IsOpen = false;
        var model = Popover.ToModel();
        await ApplyAsync(
            async () => (await _schedule.CreateAsync(model)).WithoutValue(),
            "Не удалось создать пару");
        _lastUsedDay = model.DayOfWeek;
    }

    /// <summary>«+ новый предмет» прямо из формы пары — чтобы не уходить на другую вкладку.</summary>
    private async Task CreateSubjectFromPopoverAsync()
    {
        var templates = await _reportTemplates.GetAllAsync();
        var semesterList = await _semesters.GetAllAsync();
        var active = await _semesters.GetActiveAsync();

        var editor = new SubjectEditorViewModel(existing: null, templates, semesterList, active?.Id, _workspace, _dialogs);
        if (!await _dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            return;
        }

        var subject = editor.ToModel();
        var result = await _subjects.CreateAsync(subject);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось создать предмет", result.Error.Message, ToastKind.Error);
            return;
        }

        subject.Id = result.Value;
        EnsureScaffold(subject);

        _subjectCache = await FilterBySemesterAsync(active);
        Popover.Subjects.Clear();
        foreach (var item in _subjectCache)
        {
            Popover.Subjects.Add(item);
        }

        Popover.SelectedSubject = Popover.Subjects.FirstOrDefault(x => x.Id == subject.Id);
    }

    // ---- работа с существующими парами ------------------------------------------------------

    /// <summary>Одиночный клик по плитке ведёт в Хаб предмета — правка живёт в контекст-меню.</summary>
    [RelayCommand]
    private void OpenHub(ScheduleTileViewModel? tile)
    {
        if (tile is not null)
        {
            _navigation.NavigateTo<SubjectHubViewModel>(
                new SubjectHubParameter(tile.Entry.SubjectId, tile.Entry.Id, SubjectHubTab.Schedule));
        }
    }

    /// <summary>Плитку перетащили на новый день/время.</summary>
    [RelayCommand]
    private async Task MoveTileAsync(ScheduleTileMove? move)
    {
        if (move?.Tile is not ScheduleTileViewModel tile)
        {
            return;
        }

        var duration = tile.Entry.EndTime - tile.Entry.StartTime;
        var start = tile.TimeForRow(move.RowIndex);
        var end = start.Add(duration);

        if (end <= start)
        {
            // Пара не должна переползать через полночь — просто отменяем жест.
            await RefreshAsync();
            return;
        }

        var day = OrganizerChoices.Days[Math.Clamp(move.DayColumn, 0, 6)].Value;
        tile.PreviewPlacement(day, start, end);

        var updated = Clone(tile.Entry);
        updated.DayOfWeek = day;
        updated.StartTime = start;
        updated.EndTime = end;

        await ApplyAsync(() => _schedule.UpdateAsync(updated), "Не удалось перенести пару");
    }

    /// <summary>Плитке потянули нижний край — меняется длительность.</summary>
    [RelayCommand]
    private async Task ResizeTileAsync(ScheduleTileResize? resize)
    {
        if (resize?.Tile is not ScheduleTileViewModel tile)
        {
            return;
        }

        var end = tile.Entry.StartTime.Add(tile.DurationForSpan(resize.RowSpan));
        if (end <= tile.Entry.StartTime)
        {
            await RefreshAsync();
            return;
        }

        tile.PreviewPlacement(tile.Entry.DayOfWeek, tile.Entry.StartTime, end);

        var updated = Clone(tile.Entry);
        updated.EndTime = end;

        await ApplyAsync(() => _schedule.UpdateAsync(updated), "Не удалось изменить длительность");
    }

    /// <summary>Продублировать пару на противоположную чётность недели.</summary>
    [RelayCommand]
    private async Task DuplicateToOtherParityAsync(ScheduleTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        var opposite = tile.Entry.WeekParity switch
        {
            WeekParity.Odd => WeekParity.Even,
            WeekParity.Even => WeekParity.Odd,

            // Пара идёт каждую неделю — дублировать её на «другую чётность» бессмысленно.
            _ => WeekParity.Any,
        };

        if (opposite == WeekParity.Any)
        {
            _toasts.Show(
                "Пара идёт каждую неделю",
                "Дублировать нечего — она и так есть и в числителе, и в знаменателе.",
                ToastKind.Warning);
            return;
        }

        var copy = Clone(tile.Entry);
        copy.Id = Guid.Empty;
        copy.WeekParity = opposite;

        await ApplyAsync(
            async () => (await _schedule.CreateAsync(copy)).WithoutValue(),
            "Не удалось продублировать пару");
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (_subjectCache.Count == 0)
        {
            _toasts.Show("Нет предметов", "Сначала добавьте хотя бы один предмет.", ToastKind.Warning);
            return;
        }

        var day = _lastUsedDay ?? DateTime.Today.DayOfWeek;
        var sameDay = _entryCache.Where(e => e.DayOfWeek == day).ToArray();
        (TimeOnly Start, TimeOnly End)? suggestedTime = null;
        if (sameDay.Length > 0)
        {
            var suggestion = NextPairTimeCalculator.Suggest(
                sameDay,
                DefaultDuration,
                TimeSpan.FromMinutes(_activeSemester?.DefaultBreakMinutes ?? 10),
                NextPairTimeCalculator.LunchOf(_activeSemester),
                new TimeOnly(9, 0));
            suggestedTime = (suggestion.Start, suggestion.End);
        }

        var editor = new ScheduleEntryEditorViewModel(
            _subjectCache, existing: null, presetDay: day, suggestedTime: suggestedTime);
        if (await _dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            await ApplyAsync(
                async () => (await _schedule.CreateAsync(editor.ToModel())).WithoutValue(),
                "Не удалось создать пару");
            _lastUsedDay = editor.SelectedDay.Value;
        }
    }

    [RelayCommand]
    private async Task EditAsync(ScheduleTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        var editor = new ScheduleEntryEditorViewModel(await _subjects.GetAllAsync(), tile.Entry);
        if (await _dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            await ApplyAsync(() => _schedule.UpdateAsync(editor.ToModel()), "Не удалось сохранить пару");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(ScheduleTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Удалить пару?",
            $"{tile.SubjectName}, {tile.TimeRange} — пара будет удалена из расписания.");
        if (!confirmed)
        {
            return;
        }

        await ApplyAsync(() => _schedule.DeleteAsync(tile.Entry.Id), "Не удалось удалить пару");
    }

    // ---- вспомогательное ---------------------------------------------------------------------

    private async Task ApplyAsync(Func<Task<Core.Common.Result>> action, string errorTitle)
    {
        var result = await action();
        if (result.IsFailure)
        {
            _toasts.Show(errorTitle, result.Error.Message, ToastKind.Error);
        }
        else
        {
            _scheduler.RequestReschedule();
        }

        // Перечитываем в любом случае: при отказе так снимается оптимистичное превью перетаскивания.
        await RefreshAsync();
    }

    private void EnsureScaffold(Subject subject)
    {
        try
        {
            _workspace.EnsureSubjectScaffold(subject);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Папка предмета не создана", ex.Message, ToastKind.Warning);
        }
    }

    /// <summary>Расписание показывает только предметы выбранного семестра (new_addons.md §5).</summary>
    private async Task<IReadOnlyList<Subject>> FilterBySemesterAsync(Semester? semester)
    {
        var all = await _subjects.GetAllAsync();
        if (semester is null)
        {
            return all;
        }

        var scoped = all.Where(x => x.SemesterId == semester.Id).ToArray();

        // Пока предметы не разложены по семестрам, показываем всё — иначе экран выглядел бы пустым.
        return scoped.Length > 0 ? scoped : all;
    }

    private void SyncHeader(Semester? semester)
    {
        HasSemester = semester is not null;

        if (semester is null)
        {
            WeekHeader = "Семестр не настроен — показаны все пары";
            CurrentParityText = string.Empty;
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var week = WeekParityCalculator.GetWeekNumber(semester.StartDate, today);
        var parity = WeekParityCalculator.GetParity(semester.StartDate, today, semester.FirstWeekIsOdd);

        CurrentParityText = parity == WeekParity.Odd ? "числитель" : "знаменатель";
        WeekHeader = week < 1
            ? $"{semester.Name} · начнётся {semester.StartDate.ToString(AppDateFormat.ShortDate, CultureInfo.InvariantCulture)}"
            : semester.EndDate is { } end && today > end
                ? $"{semester.Name} · семестр завершён"
                : $"{semester.Name} · неделя {week}";
    }

    /// <summary>Линия «сейчас» и подсветка идущей пары.</summary>
    private void UpdateNowMarkers(Semester? semester)
    {
        var now = DateTime.Now;
        var nowTime = TimeOnly.FromDateTime(now);

        TodayColumn = ((int)now.DayOfWeek + 6) % 7;

        var minutes = (nowTime - new TimeOnly(DayStartHour, 0)).TotalMinutes;
        var totalMinutes = (DayEndHour - DayStartHour) * 60;
        NowLineOffset = minutes >= 0 && minutes <= totalMinutes
            ? minutes / SlotMinutes * SlotPixelHeight
            : -1;

        var today = DateOnly.FromDateTime(now);
        foreach (var tile in Tiles)
        {
            var appliesToday = tile.IsToday
                && (tile.Entry.WeekParity == WeekParity.Any
                    || semester is null
                    || WeekParityCalculator.GetParity(
                        semester.StartDate, today, semester.FirstWeekIsOdd) == tile.Entry.WeekParity);

            tile.IsCurrent = appliesToday
                && tile.Entry.StartTime <= nowTime
                && nowTime < tile.Entry.EndTime;
        }
    }

    /// <summary>
    /// Копия записи для правки: менять экземпляр, на который смотрит плитка, до подтверждения из БД
    /// не стоит — иначе неудачное сохранение оставит на экране несуществующее состояние.
    /// </summary>
    private static ScheduleEntry Clone(ScheduleEntry entry) => new()
    {
        Id = entry.Id,
        SubjectId = entry.SubjectId,
        DayOfWeek = entry.DayOfWeek,
        StartTime = entry.StartTime,
        EndTime = entry.EndTime,
        Room = entry.Room,
        Teacher = entry.Teacher,
        Type = entry.Type,
        WeekParity = entry.WeekParity,
    };
}
