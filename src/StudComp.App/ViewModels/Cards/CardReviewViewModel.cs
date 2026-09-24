using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Вкладка «Повторение» (new_addons.md §2.2, §6.3): очередь на сегодня, разбивка по предметам и
/// обратный отсчёт к экзаменам.
/// </summary>
public sealed partial class CardReviewViewModel : ObservableObject
{
    private readonly IReviewQueueService _queue;
    private readonly ICardService _cards;
    private readonly ICramPlanService _cram;
    private readonly ISubjectService _subjects;
    private readonly INavigationService _navigation;
    private readonly IToastService _toasts;

    public CardReviewViewModel(
        IReviewQueueService queue,
        ICardService cards,
        ICramPlanService cram,
        ISubjectService subjects,
        INavigationService navigation,
        IToastService toasts)
    {
        _queue = queue;
        _cards = cards;
        _cram = cram;
        _subjects = subjects;
        _navigation = navigation;
        _toasts = toasts;
    }

    /// <summary>Разбивка очереди по предметам — запуск можно начать с одного из них.</summary>
    public ObservableCollection<ReviewSubjectRowViewModel> Subjects { get; } = [];

    /// <summary>Обратный отсчёт к ближайшим экзаменам (new_addons.md §6.4).</summary>
    public ObservableCollection<CramRowViewModel> Countdowns { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQueue))]
    [NotifyPropertyChangedFor(nameof(DueText))]
    private int _dueCount;

    [ObservableProperty]
    private int _newCount;

    [ObservableProperty]
    private int _learningCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverLimit))]
    [NotifyPropertyChangedFor(nameof(OverLimitText))]
    private int _overLimitCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextDue))]
    private string? _nextDueText;

    public bool HasQueue => DueCount > 0 || NewCount > 0;

    public bool HasNextDue => !string.IsNullOrEmpty(NextDueText);

    public bool HasOverLimit => OverLimitCount > 0;

    public bool HasCountdowns => Countdowns.Count > 0;

    public string DueText => Plural(DueCount, "карточка", "карточки", "карточек");

    public string OverLimitText =>
        $"Сверх дневного лимита осталось ещё {Plural(OverLimitCount, "карточка", "карточки", "карточек")}.";

    /// <summary>Перечитать очередь и обратные отсчёты.</summary>
    public async Task RefreshAsync()
    {
        IsBusy = true;

        try
        {
            var counts = await _queue.GetCountsAsync().ConfigureAwait(true);
            DueCount = counts.Due;
            NewCount = counts.New;
            LearningCount = Math.Max(0, counts.Due - counts.New);

            var snapshot = await _queue.BuildAsync().ConfigureAwait(true);
            OverLimitCount = snapshot.IsSuccess ? snapshot.Value.SkippedByLimit : 0;

            var next = snapshot.IsSuccess ? snapshot.Value.NextDueAt : null;
            NextDueText = DueCount == 0 && next is { } moment ? FormatNext(moment) : null;

            await LoadSubjectsAsync(snapshot.IsSuccess ? snapshot.Value.CardIds : []).ConfigureAwait(true);
            await LoadCountdownsAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Разложить уже собранную очередь по предметам. Именно так, а не запросом на каждый предмет:
    /// строить очередь по разу на предмет — это N походов в базу ради чисел на одном экране.
    /// </summary>
    private async Task LoadSubjectsAsync(IReadOnlyList<Guid> queued)
    {
        if (queued.Count == 0)
        {
            Subjects.Clear();
            return;
        }

        var cards = await _cards.GetByIdsAsync(queued).ConfigureAwait(true);
        var all = await _subjects.GetAllAsync().ConfigureAwait(true);

        var counts = cards
            .Where(c => c.SubjectId is not null)
            .GroupBy(c => c.SubjectId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // Коллекции заменяются одним синхронным блоком после последнего await: запросы идут на
        // пуле, и наложившиеся обновления иначе давали бы дубли (Phase 13.10).
        Subjects.Clear();
        foreach (var subject in all.Where(s => counts.ContainsKey(s.Id)).OrderByDescending(s => counts[s.Id]))
        {
            Subjects.Add(new ReviewSubjectRowViewModel(
                subject.Id, subject.Name, subject.ColorHex, counts[subject.Id]));
        }
    }

    private async Task LoadCountdownsAsync()
    {
        var statuses = await _cram.GetAllAsync().ConfigureAwait(true);

        Countdowns.Clear();
        foreach (var status in statuses)
        {
            Countdowns.Add(new CramRowViewModel(status));
        }

        OnPropertyChanged(nameof(HasCountdowns));
    }

    /// <summary>Начать повторение всей очереди.</summary>
    [RelayCommand]
    private void StartReview() => StartReviewFor(null);

    /// <summary>Начать повторение одного предмета.</summary>
    [RelayCommand]
    private void StartSubject(ReviewSubjectRowViewModel? row)
    {
        if (row is not null)
        {
            StartReviewFor(row.SubjectId);
        }
    }

    /// <summary>Готовиться к экзамену — режим аврала (new_addons.md §6.4).</summary>
    [RelayCommand]
    private async Task PrepareAsync(CramRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var today = await _cram.GetTodayAsync(row.SubjectId).ConfigureAwait(true);

        if (today.IsFailure)
        {
            _toasts.Show("Аврал", today.Error.Message, ToastKind.Warning);
            return;
        }

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Cram,
            StudySessionFilter.Empty with
            {
                CardIds = today.Value,
                Order = StudyOrder.HardestFirst,
            },
            Title: $"Аврал · {row.SubjectName}"));
    }

    private void StartReviewFor(Guid? subjectId)
    {
        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Review,
            StudySessionFilter.Empty with
            {
                SubjectIds = subjectId is { } id ? [id] : [],
                Order = StudyOrder.DueFirst,
            },
            Title: "Повторение"));
    }

    private static string FormatNext(DateTimeOffset when)
    {
        var days = (when.Date - DateTimeOffset.Now.Date).Days;

        return days switch
        {
            <= 0 => "Следующее повторение — сегодня",
            1 => "Следующее повторение — завтра",
            _ => $"Следующее повторение — {when:dd MMMM}",
        };
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

/// <summary>Строка предмета в очереди повторения.</summary>
public sealed class ReviewSubjectRowViewModel(Guid subjectId, string name, string? colorHex, int dueCount)
{
    public Guid SubjectId { get; } = subjectId;

    public string SubjectName { get; } = name;

    public System.Windows.Media.Brush AccentBrush { get; } = Organizer.SubjectColor.BrushFor(colorHex);

    public int DueCount { get; } = dueCount;
}

/// <summary>Строка обратного отсчёта к экзамену.</summary>
public sealed class CramRowViewModel(CramStatus status)
{
    public Guid SubjectId { get; } = status.SubjectId;

    public string SubjectName { get; } = status.SubjectName;

    public string Title { get; } =
        $"До экзамена по «{status.SubjectName}» {status.DaysLeft} дн · сегодня {status.CardsToday} карточек";

    public int ProgressPercent { get; } = status.ProgressPercent;

    public string ProgressText { get; } = $"Пройдено {status.ProgressPercent} %";

    /// <summary>
    /// Честная строка вместо бодрой: если K показов не помещается, план так и говорит
    /// (new_addons.md §6.4).
    /// </summary>
    public string? Warning { get; } = status.IsAchievable
        ? null
        : $"Успеваем показать каждую {status.AchievableShows} раза вместо {status.RequestedShows}.";

    public bool HasWarning => Warning is not null;
}
