using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Resources;
using StudComp.Services;
using StudComp.ViewModels.Organizer;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Вкладка «Экзамен» (new_addons.md §5.6): конструктор пробного экзамена и результаты прошлых.
/// </summary>
public sealed partial class CardExamViewModel : ObservableObject
{
    /// <summary>Сколько прошлых сессий показывать в истории.</summary>
    private const int HistorySize = 15;

    private readonly AsyncGate _refreshGate = new();
    private readonly IStudySessionService _sessions;
    private readonly IStudySessionRepository _sessionRepo;
    private readonly ICardTagService _tags;
    private readonly ISubjectService _subjects;
    private readonly ICardDeckService _decks;
    private readonly INavigationService _navigation;
    private readonly IToastService _toasts;

    public CardExamViewModel(
        IStudySessionService sessions,
        IStudySessionRepository sessionRepo,
        ICardTagService tags,
        ISubjectService subjects,
        ICardDeckService decks,
        INavigationService navigation,
        IToastService toasts,
        IOptionsMonitor<CardsOptions> options)
    {
        _sessions = sessions;
        _sessionRepo = sessionRepo;
        _tags = tags;
        _subjects = subjects;
        _decks = decks;
        _navigation = navigation;
        _toasts = toasts;

        var settings = options.CurrentValue;
        _selectedCount = CardChoices.ExamSizes.FirstOrDefault(c => c.Value == settings.DefaultExamCardCount)
            ?? CardChoices.ExamSizes[1];
        _selectedMethod = CardChoices.CheckModes.First(m => m.Value == settings.DefaultCheckMode);
        _selectedTimeLimit = CardChoices.TimeLimits[0];
        _reverseSides = settings.ReverseByDefault;
        _seedText = string.Empty;
    }

    /// <summary>Предметы билета — мультивыбор.</summary>
    public ObservableCollection<ExamPickViewModel> Subjects { get; } = [];

    /// <summary>Колоды билета — мультивыбор.</summary>
    public ObservableCollection<ExamPickViewModel> Decks { get; } = [];

    /// <summary>Метки билета — мультивыбор.</summary>
    public ObservableCollection<ExamPickViewModel> Tags { get; } = [];

    /// <summary>Прошлые экзамены с точностью и кнопками повтора.</summary>
    public ObservableCollection<ExamHistoryRowViewModel> History { get; } = [];

    public IReadOnlyList<NamedChoice<int>> Sizes => CardChoices.ExamSizes;

    public IReadOnlyList<NamedChoice<StudyCheckMode>> Methods => CardChoices.CheckModes;

    public IReadOnlyList<NamedChoice<int>> TimeLimits => CardChoices.TimeLimits;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private NamedChoice<int> _selectedCount;

    [ObservableProperty]
    private NamedChoice<StudyCheckMode> _selectedMethod;

    [ObservableProperty]
    private NamedChoice<int> _selectedTimeLimit;

    [ObservableProperty]
    private bool _includeSuspended;

    [ObservableProperty]
    private bool _reverseSides;

    /// <summary>
    /// Зерно рандомизации. По умолчанию пустое — новое случайное; введённое даёт в точности тот же
    /// билет, что и раньше (new_addons.md §5.4).
    /// </summary>
    [ObservableProperty]
    private string _seedText;

    [ObservableProperty]
    private bool _hasHistory;

    /// <summary>Перечитать справочники и историю.</summary>
    public async Task RefreshAsync()
    {
        // Списки чистятся до await — наложение двух обновлений дало бы дубли (Phase 13.10).
        using var gate = await _refreshGate.EnterAsync();
        IsBusy = true;

        try
        {
            await LoadPicksAsync().ConfigureAwait(true);
            await LoadHistoryAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPicksAsync()
    {
        var chosenSubjects = Subjects.Where(x => x.IsChosen).Select(x => x.Id).ToHashSet();
        var chosenDecks = Decks.Where(x => x.IsChosen).Select(x => x.Id).ToHashSet();
        var chosenTags = Tags.Where(x => x.IsChosen).Select(x => x.Id).ToHashSet();

        Subjects.Clear();
        foreach (var subject in await _subjects.GetAllAsync().ConfigureAwait(true))
        {
            Subjects.Add(new ExamPickViewModel(subject.Id, subject.Name, SubjectColor.BrushFor(subject.ColorHex))
            {
                IsChosen = chosenSubjects.Contains(subject.Id),
            });
        }

        Decks.Clear();
        foreach (var deck in await _decks.GetAllAsync().ConfigureAwait(true))
        {
            Decks.Add(new ExamPickViewModel(deck.Id, deck.Name, null) { IsChosen = chosenDecks.Contains(deck.Id) });
        }

        Tags.Clear();
        foreach (var tag in await _tags.GetTopAsync(20).ConfigureAwait(true))
        {
            Tags.Add(new ExamPickViewModel(tag.Id, tag.DisplayName, null) { IsChosen = chosenTags.Contains(tag.Id) });
        }
    }

    private async Task LoadHistoryAsync()
    {
        History.Clear();

        var recent = await _sessionRepo.GetRecentByModeAsync(StudyMode.Exam, HistorySize).ConfigureAwait(true);

        foreach (var session in recent.Where(s => s.AnsweredCount > 0))
        {
            History.Add(new ExamHistoryRowViewModel(session));
        }

        HasHistory = History.Count > 0;
    }

    /// <summary>Начать экзамен по собранному билету.</summary>
    [RelayCommand]
    private void Start()
    {
        var seed = int.TryParse(SeedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (int?)null;

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Exam,
            BuildFilter(),
            seed,
            "Пробный экзамен"));
    }

    /// <summary>Тренировка по тому же билету — без таймера и без влияния на расписание.</summary>
    [RelayCommand]
    private void StartPractice() =>
        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Practice,
            BuildFilter() with { TimeLimitSeconds = null, PerCardLimitSeconds = null },
            Title: "Тренировка"));

    /// <summary>«Ещё раз то же самое» — тот же билет с тем же зерном.</summary>
    [RelayCommand]
    private async Task RepeatAsync(ExamHistoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var result = await _sessions.RepeatAsync(row.SessionId).ConfigureAwait(true);

        if (result.IsFailure)
        {
            _toasts.Show("Экзамен", result.Error.Message, ToastKind.Warning);
            return;
        }

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            result.Value.Mode,
            result.Value.Filter,
            result.Value.Seed,
            "Пробный экзамен"));
    }

    /// <summary>Работа над ошибками конкретного экзамена.</summary>
    [RelayCommand]
    private async Task WorkOnMistakesAsync(ExamHistoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var result = await _sessions.StartMistakesAsync(row.SessionId).ConfigureAwait(true);

        if (result.IsFailure)
        {
            _toasts.Show("Работа над ошибками", result.Error.Message, ToastKind.Warning);
            return;
        }

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Mistakes,
            result.Value.Filter,
            result.Value.Seed,
            "Работа над ошибками"));
    }

    /// <summary>Снять весь выбор в билете.</summary>
    [RelayCommand]
    private void ClearPicks()
    {
        foreach (var pick in Subjects.Concat(Decks).Concat(Tags))
        {
            pick.IsChosen = false;
        }
    }

    private StudySessionFilter BuildFilter() => StudySessionFilter.Empty with
    {
        SubjectIds = [.. Subjects.Where(x => x.IsChosen).Select(x => x.Id)],
        DeckIds = [.. Decks.Where(x => x.IsChosen).Select(x => x.Id)],
        TagIds = [.. Tags.Where(x => x.IsChosen).Select(x => x.Id)],
        IncludeSuspended = IncludeSuspended,
        ReverseSides = ReverseSides,
        CheckMode = SelectedMethod.Value,
        Order = StudyOrder.Random,
        MaxCards = SelectedCount.Value,
        TimeLimitSeconds = SelectedTimeLimit.Value > 0 ? SelectedTimeLimit.Value : null,
        PerCardLimitSeconds = SelectedTimeLimit.Value < 0 ? -SelectedTimeLimit.Value : null,
        AffectsScheduling = false,
    };
}

/// <summary>Пункт мультивыбора в конструкторе билета.</summary>
public sealed partial class ExamPickViewModel(Guid id, string name, System.Windows.Media.Brush? accent)
    : ObservableObject
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    public System.Windows.Media.Brush? Accent { get; } = accent;

    [ObservableProperty]
    private bool _isChosen;
}

/// <summary>Строка истории экзаменов.</summary>
public sealed class ExamHistoryRowViewModel(StudySession session)
{
    public Guid SessionId { get; } = session.Id;

    public string When { get; } =
        session.StartedAt.LocalDateTime.ToString($"{AppDateFormat.ShortDate} HH:mm", CultureInfo.InvariantCulture);

    public string ScoreText { get; } = $"{session.CorrectCount} / {session.AnsweredCount}";

    public string AccuracyText { get; } = session.AnsweredCount == 0
        ? "—"
        : $"{100.0 * session.CorrectCount / session.AnsweredCount:0} %";

    public bool HasMisses { get; } = session.AnsweredCount > session.CorrectCount;
}
