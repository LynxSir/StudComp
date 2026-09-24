using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using StudComp.Core.Domain;
using StudComp.Modules.Organizer.Services;
using StudComp.ViewModels.Shell;

namespace StudComp.ViewModels.Organizer.Hub;

/// <summary>Строка разбивки часов по типу занятий.</summary>
public sealed record HubHoursRow(string TypeText, int Classes, string HoursText, string HeldText);

/// <summary>
/// Вкладка «Обзор» Хаба предмета (new_addons.md §5): ориентировочные часы за семестр с разбивкой по
/// типам и «проведено / осталось», ближайшая пара и мини-список дедлайнов.
/// </summary>
public sealed partial class HubOverviewViewModel(
    IScheduleService schedule,
    IDeadlineService deadlines,
    ISemesterService semesters,
    IMessenger messenger) : ObservableObject
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private readonly AsyncGate _loadGate = new();
    private Guid _subjectId;

    /// <summary>Разбивка часов по типам занятий.</summary>
    public ObservableCollection<HubHoursRow> Hours { get; } = [];

    /// <summary>Открытые дедлайны предмета, ближайшие сверху.</summary>
    public ObservableCollection<DeadlineRowViewModel> Deadlines { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Часы посчитаны — семестр настроен и у него есть дата окончания.</summary>
    [ObservableProperty]
    private bool _hasHours;

    /// <summary>Почему часов нет: не настроен семестр либо не задана дата окончания.</summary>
    [ObservableProperty]
    private string _hoursUnavailableText = string.Empty;

    [ObservableProperty]
    private string _totalHoursText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string _nextClassText = string.Empty;

    [ObservableProperty]
    private bool _hasNextClass;

    public bool HasDeadlines => Deadlines.Count > 0;

    public async Task LoadAsync(Guid subjectId)
    {
        // Списки чистятся до await — наложение двух загрузок дало бы дубли (Phase 13.10).
        using var gate = await _loadGate.EnterAsync();
        _subjectId = subjectId;
        IsBusy = true;
        try
        {
            await LoadHoursAsync().ConfigureAwait(true);
            await LoadDeadlinesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Открыть настройки семестра — дип-линк из плашки «дата окончания не задана».</summary>
    [RelayCommand]
    private void OpenSemesterSettings() =>
        messenger.Send(new OpenSettingsMessage(SettingsSection.Semester));

    private async Task LoadHoursAsync()
    {
        Hours.Clear();
        HasHours = false;
        HasNextClass = false;

        var semester = await semesters.GetActiveAsync().ConfigureAwait(true);
        if (semester is null)
        {
            HoursUnavailableText = "Семестр не настроен — часы посчитать не от чего.";
            return;
        }

        if (semester.EndDate is not { } end)
        {
            // Придумывать длительность семестра нельзя — честно просим её задать.
            HoursUnavailableText =
                $"У семестра «{semester.Name}» не задана дата окончания — без неё часы не посчитать.";
            return;
        }

        var entries = (await schedule.GetBySubjectAsync(_subjectId).ConfigureAwait(true)).ToArray();
        if (entries.Length == 0)
        {
            HoursUnavailableText = "У предмета пока нет пар в расписании.";
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var summary = AcademicHoursCalculator.Summarize(
            entries, semester.StartDate, end, today, semester.FirstWeekIsOdd);

        foreach (var row in summary.ByType)
        {
            Hours.Add(new HubHoursRow(
                OrganizerChoices.ScheduleTypeName(row.Type),
                row.Classes,
                $"≈ {FormatHours(row.TotalHours)} ч",
                $"{FormatHours(row.HeldHours)} из {FormatHours(row.TotalHours)} ч"));
        }

        HasHours = true;
        TotalHoursText = $"≈ {FormatHours(summary.TotalHours)} ч за семестр · {summary.TotalClasses} пар";
        ProgressText =
            $"Проведено {summary.HeldClasses} из {summary.TotalClasses} · осталось {summary.RemainingClasses}";
        ProgressFraction = summary.TotalClasses == 0
            ? 0
            : (double)summary.HeldClasses / summary.TotalClasses;

        if (summary.NextClassDate is { } next)
        {
            HasNextClass = true;
            NextClassText = next == today
                ? "Ближайшая пара — сегодня"
                : $"Ближайшая пара — {next.ToString("dd MMMM", Ru)}";
        }
    }

    private async Task LoadDeadlinesAsync()
    {
        Deadlines.Clear();

        var now = DateTimeOffset.Now;
        var all = await deadlines.GetBySubjectAsync(_subjectId).ConfigureAwait(true);

        foreach (var deadline in all
            .Where(x => x.Status == DeadlineStatus.Pending)
            .OrderBy(x => x.DueDate)
            .Take(5))
        {
            Deadlines.Add(new DeadlineRowViewModel(deadline, string.Empty, null, now));
        }

        OnPropertyChanged(nameof(HasDeadlines));
    }

    /// <summary>Часы округляем до половины — «≈ 31,5 ч» честнее, чем ложная точность до минут.</summary>
    private static string FormatHours(double hours) =>
        (Math.Round(hours * 2, MidpointRounding.AwayFromZero) / 2).ToString("0.#", Ru);
}
