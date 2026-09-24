using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Controls;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;
using StudComp.Services;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Вкладка «Статистика» (new_addons.md §6.5): календарь активности, точность по предметам и меткам,
/// слабые места, кривая нагрузки вперёд и серия дней.
/// </summary>
public sealed partial class CardStatsViewModel : ObservableObject
{
    /// <summary>Сколько слабых карточек показывать.</summary>
    private const int WeakCardsTake = 15;

    private readonly AsyncGate _refreshGate = new();
    private readonly ICardStatisticsService _stats;
    private readonly INavigationService _navigation;
    private readonly IToastService _toasts;
    private readonly IOptionsMonitor<CardsOptions> _options;

    public CardStatsViewModel(
        ICardStatisticsService stats,
        INavigationService navigation,
        IToastService toasts,
        IOptionsMonitor<CardsOptions> options)
    {
        _stats = stats;
        _navigation = navigation;
        _toasts = toasts;
        _options = options;

        _selectedPeriod = CardChoices.StatPeriods[1];
    }

    public IReadOnlyList<NamedChoice<int>> Periods => CardChoices.StatPeriods;

    /// <summary>Точность по предметам за выбранный период.</summary>
    public ObservableCollection<StatRowViewModel> BySubject { get; } = [];

    /// <summary>Точность по меткам, худшие сверху.</summary>
    public ObservableCollection<StatRowViewModel> ByTag { get; } = [];

    /// <summary>Слабые места — кнопка «Гонять их» берёт именно их.</summary>
    public ObservableCollection<StatRowViewModel> WeakCards { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private NamedChoice<int> _selectedPeriod;

    partial void OnSelectedPeriodChanged(NamedChoice<int> value) => _ = RefreshAsync();

    /// <summary>Данные календаря активности — рисует <see cref="ActivityHeatmap"/>.</summary>
    [ObservableProperty]
    private ActivityHeatmapData? _calendar;

    /// <summary>Данные кривой нагрузки — рисует <see cref="ReviewForecastChart"/>.</summary>
    [ObservableProperty]
    private ReviewForecastData? _forecast;

    [ObservableProperty]
    private string _streakText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWeakCards))]
    private bool _hasWeakCardsValue;

    [ObservableProperty]
    private bool _hasAnyHistory;

    public bool HasWeakCards => WeakCards.Count > 0;

    /// <summary>Перечитать всю статистику.</summary>
    public async Task RefreshAsync()
    {
        // Списки чистятся до await — наложение двух обновлений дало бы дубли (Phase 13.10).
        using var gate = await _refreshGate.EnterAsync();
        IsBusy = true;

        try
        {
            var days = SelectedPeriod.Value;
            var today = StudyDay.DayOf(DateTimeOffset.Now, _options.CurrentValue.DayRolloverHour);

            var calendar = await _stats.GetActivityCalendarAsync(today.AddDays(-364), today).ConfigureAwait(true);
            Calendar = new ActivityHeatmapData(
                today.AddDays(-364),
                today,
                calendar.ToDictionary(d => d.Day, d => d.Answers));

            HasAnyHistory = calendar.Count > 0;

            var streak = await _stats.GetStreakAsync().ConfigureAwait(true);
            StreakText = streak.Current == 0
                ? "Серия прервана — самое время начать заново."
                : $"Серия: {Plural(streak.Current, "день", "дня", "дней")} подряд (лучшая — {streak.Longest}).";

            TotalText = $"Ответов за год: {calendar.Sum(d => d.Answers)}.";

            BySubject.Clear();
            foreach (var row in await _stats.GetAccuracyBySubjectAsync(days).ConfigureAwait(true))
            {
                BySubject.Add(new StatRowViewModel(row.Id, row.Name, row.Answers, row.Correct));
            }

            ByTag.Clear();
            foreach (var row in await _stats.GetAccuracyByTagAsync(days).ConfigureAwait(true))
            {
                ByTag.Add(new StatRowViewModel(row.Id, row.Name, row.Answers, row.Correct));
            }

            WeakCards.Clear();
            foreach (var row in await _stats.GetWeakCardsAsync(days, WeakCardsTake).ConfigureAwait(true))
            {
                WeakCards.Add(new StatRowViewModel(row.Id, row.Name, row.Answers, row.Correct));
            }

            HasWeakCardsValue = WeakCards.Count > 0;
            OnPropertyChanged(nameof(HasWeakCards));

            var forecast = await _stats.GetLoadForecastAsync().ConfigureAwait(true);
            Forecast = new ReviewForecastData(
                today,
                30,
                forecast.ToDictionary(d => d.Day, d => d.Count));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>«Гонять их» — тренировка ровно по слабым карточкам.</summary>
    [RelayCommand(CanExecute = nameof(HasWeakCards))]
    private void DrillWeak()
    {
        var ids = WeakCards.Where(r => r.Id is not null).Select(r => r.Id!.Value).ToList();

        if (ids.Count == 0)
        {
            _toasts.Show("Слабые места", "Пока нечего гонять — статистика ещё не набралась.", ToastKind.Info);
            return;
        }

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Practice,
            StudySessionFilter.Empty with { CardIds = ids, Order = StudyOrder.HardestFirst },
            Title: "Слабые места"));
    }

    private static string Plural(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;

        var word = mod100 is >= 11 and <= 14
            ? many
            : mod10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };

        return $"{count} {word}";
    }
}

/// <summary>Строка точности: предмет, метка или карточка.</summary>
public sealed class StatRowViewModel(Guid? id, string name, int answers, int correct)
{
    public Guid? Id { get; } = id;

    public string Name { get; } = name;

    public int Answers { get; } = answers;

    public int Correct { get; } = correct;

    public double Ratio { get; } = answers == 0 ? 0 : (double)correct / answers;

    public string Text => $"{Correct} / {Answers} · {Ratio * 100:0} %";
}
