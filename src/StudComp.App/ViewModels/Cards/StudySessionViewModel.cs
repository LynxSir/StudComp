using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using StudComp.Controls;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;
using StudComp.Services;
using StudComp.ViewModels.Organizer;
using StudComp.ViewModels.Shell;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace StudComp.ViewModels.Cards;

/// <summary>Что показывает страница сессии прямо сейчас.</summary>
public enum StudySessionPhase
{
    /// <summary>Идёт сборка плана.</summary>
    Loading = 0,

    /// <summary>Карточка за карточкой.</summary>
    Running = 1,

    /// <summary>Итоги — отдельный экран, а не тост (new_addons.md §5.6).</summary>
    Finished = 2,
}

/// <summary>
/// Параметр перехода на экран сессии.
/// </summary>
/// <param name="Mode">Режим (new_addons.md §5.5).</param>
/// <param name="Filter">Что и как гонять.</param>
/// <param name="Seed">Зерно; <see langword="null"/> — новое случайное.</param>
/// <param name="Title">Подпись в шапке — «Повторение», «Пробный экзамен», «Аврал: Матан».</param>
public sealed record StudySessionParameter(
    StudyMode Mode,
    StudySessionFilter Filter,
    int? Seed = null,
    string? Title = null);

/// <summary>
/// Экран сессии (new_addons.md §5.1, §8.3) — отдельная полноэкранная страница, а не вкладка: во
/// время проверки себя на экране не должно быть ничего, кроме карточки.
/// </summary>
/// <remarks>
/// Вьюмодель тонкая: состояние одной карточки держит <see cref="StudyCardPresenter"/> из модуля,
/// и разметка биндится <b>только</b> к его свойствам. Благодаря этому правило «ответа не существует
/// до раскрытия» проверяется машинно, а не на глаз.
/// </remarks>
public sealed partial class StudySessionViewModel : ObservableObject, INavigationAware, IDisposable
{
    /// <summary>За сколько секунд до конца полоса таймера меняет цвет (new_addons.md §5.6).</summary>
    private const int TimeWarningSeconds = 60;

    /// <summary>Через сколько карточек после провала показывать её снова.</summary>
    private const int RelearnGap = 3;

    private readonly IStudySessionService _sessions;
    private readonly ICardService _cards;
    private readonly ICardDeckService _decks;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly INavigationService _navigation;
    private readonly IMessenger _messenger;
    private readonly IMarkdownDocumentModelBuilder _markdown;
    private readonly IOptionsMonitor<CardsOptions> _options;

    private readonly Dictionary<Guid, StudyCardView> _prepared = [];
    private readonly Stopwatch _cardWatch = new();
    private readonly DispatcherTimer _timer;

    private StudySessionPlan? _plan;
    private List<Guid> _order = [];
    private int _index;
    private StudyCardPresenter? _presenter;
    private StudyCardView? _view;
    private DateTimeOffset _startedAt;
    private bool _finishing;

    public StudySessionViewModel(
        IStudySessionService sessions,
        ICardService cards,
        ICardDeckService decks,
        IDialogService dialogs,
        IToastService toasts,
        INavigationService navigation,
        IMessenger messenger,
        IMarkdownDocumentModelBuilder markdown,
        IOptionsMonitor<CardsOptions> options)
    {
        _sessions = sessions;
        _cards = cards;
        _decks = decks;
        _dialogs = dialogs;
        _toasts = toasts;
        _navigation = navigation;
        _messenger = messenger;
        _markdown = markdown;
        _options = options;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateTimer();
    }

    // ---- общее состояние экрана ---------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private StudySessionPhase _phase = StudySessionPhase.Loading;

    public bool IsLoading => Phase == StudySessionPhase.Loading;

    public bool IsRunning => Phase == StudySessionPhase.Running;

    public bool IsFinished => Phase == StudySessionPhase.Finished;

    [ObservableProperty]
    private string _title = "Сессия";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimer))]
    private string? _timerText;

    public bool HasTimer => !string.IsNullOrEmpty(TimerText);

    [ObservableProperty]
    private bool _isTimeWarning;

    [ObservableProperty]
    private string? _emptyMessage;

    // ---- текущая карточка ---------------------------------------------------------------------

    [ObservableProperty]
    private string _questionText = string.Empty;

    /// <summary>
    /// Оборот карточки. Пока карточка не раскрыта — <see langword="null"/>: разметка привязана к
    /// этому свойству, поэтому в дереве элементов ответа физически нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswer))]
    private FlowDocument? _answerDocument;

    public bool HasAnswer => AnswerDocument is not null;

    [ObservableProperty]
    private string? _sourceText;

    [ObservableProperty]
    private string? _intervalHint;

    [ObservableProperty]
    private string _subjectName = string.Empty;

    [ObservableProperty]
    private Brush _accentBrush = Brushes.Transparent;

    [ObservableProperty]
    private SymbolRegular _kindIcon = SymbolRegular.Layer24;

    [ObservableProperty]
    private IReadOnlyList<string> _tags = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowHint))]
    private bool _hasHint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowHint))]
    private string? _hintText;

    public bool CanShowHint => HasHint && string.IsNullOrEmpty(HintText);

    [ObservableProperty]
    private bool _isRevealed;

    // ---- способ проверки ------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelfAssessment))]
    [NotifyPropertyChangedFor(nameof(IsMultipleChoice))]
    [NotifyPropertyChangedFor(nameof(IsTypedAnswer))]
    [NotifyPropertyChangedFor(nameof(IsCloze))]
    private StudyCheckMode _checkMode = StudyCheckMode.SelfAssessment;

    public bool IsSelfAssessment => CheckMode == StudyCheckMode.SelfAssessment;

    public bool IsMultipleChoice => CheckMode == StudyCheckMode.MultipleChoice;

    public bool IsTypedAnswer => CheckMode == StudyCheckMode.TypedAnswer;

    public bool IsCloze => CheckMode == StudyCheckMode.Cloze;

    public ObservableCollection<StudyOptionViewModel> Options { get; } = [];

    public ObservableCollection<StudyGradeViewModel> Grades { get; } = [];

    [ObservableProperty]
    private string _typedAnswer = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerdict))]
    private string? _verdictText;

    public bool HasVerdict => !string.IsNullOrEmpty(VerdictText);

    [ObservableProperty]
    private bool _canAcceptAsCorrect;

    [ObservableProperty]
    private string? _clozeText;

    [ObservableProperty]
    private bool _hasHiddenGaps;

    // ---- итоги -----------------------------------------------------------------------------------

    [ObservableProperty]
    private StudySessionSummary? _summary;

    [ObservableProperty]
    private string _scoreText = string.Empty;

    [ObservableProperty]
    private string _accuracyText = string.Empty;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    public ObservableCollection<StudyScoreRowViewModel> SubjectScores { get; } = [];

    public ObservableCollection<StudyScoreRowViewModel> DeckScores { get; } = [];

    public ObservableCollection<StudyScoreRowViewModel> WeakTags { get; } = [];

    public ObservableCollection<StudyMissViewModel> Missed { get; } = [];

    // ---- жизненный цикл ---------------------------------------------------------------------------

    /// <inheritdoc />
    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is StudySessionParameter request)
        {
            Title = request.Title ?? TitleFor(request.Mode);
            _ = StartAsync(request);
        }
        else
        {
            Phase = StudySessionPhase.Finished;
            EmptyMessage = "Сессия не задана.";
        }
    }

    /// <inheritdoc />
    public void OnNavigatedFrom()
    {
        _timer.Stop();
    }

    private async Task StartAsync(StudySessionParameter request)
    {
        Phase = StudySessionPhase.Loading;

        var result = await _sessions
            .StartAsync(new StudySessionRequest(request.Mode, request.Filter, request.Seed))
            .ConfigureAwait(true);

        if (result.IsFailure)
        {
            Phase = StudySessionPhase.Finished;
            EmptyMessage = result.Error.Message;
            return;
        }

        _plan = result.Value;
        _order = [.. _plan.CardIds];
        _index = 0;
        _startedAt = DateTimeOffset.Now;

        if (_plan.TimeLimitSeconds is not null || _plan.PerCardLimitSeconds is not null)
        {
            _timer.Start();
        }

        Phase = StudySessionPhase.Running;
        await ShowCurrentAsync().ConfigureAwait(true);
    }

    private async Task ShowCurrentAsync()
    {
        if (_plan is null || _index >= _order.Count)
        {
            await FinishAsync().ConfigureAwait(true);
            return;
        }

        var cardId = _order[_index];
        var view = await GetViewAsync(cardId).ConfigureAwait(true);

        if (view is null)
        {
            // Карточка исчезла между сборкой плана и показом — просто идём дальше.
            _order.RemoveAt(_index);
            await ShowCurrentAsync().ConfigureAwait(true);
            return;
        }

        _view = view;
        _presenter = new StudyCardPresenter(view);

        ApplyPresenter();
        UpdateProgress();

        _cardWatch.Restart();

        // Следующая карточка готовится, пока пользователь думает над этой (< 100 мс на переход).
        _ = PrefetchAsync(_index + 1);
    }

    private async Task<StudyCardView?> GetViewAsync(Guid cardId)
    {
        if (_prepared.TryGetValue(cardId, out var cached))
        {
            return cached;
        }

        var result = await _sessions.PrepareCardAsync(cardId, _plan!).ConfigureAwait(true);
        if (result.IsFailure)
        {
            return null;
        }

        _prepared[cardId] = result.Value;
        return result.Value;
    }

    private async Task PrefetchAsync(int index)
    {
        if (_plan is null || index >= _order.Count)
        {
            return;
        }

        try
        {
            await GetViewAsync(_order[index]).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Предзагрузка — удобство, а не обязанность: сорвалась, значит подготовим по месту.
        }
    }

    private void ApplyPresenter()
    {
        var presenter = _presenter!;
        var view = _view!;

        QuestionText = presenter.Question;
        AnswerDocument = null;
        SourceText = null;
        IntervalHint = null;
        IsRevealed = false;

        SubjectName = view.SubjectName ?? string.Empty;
        AccentBrush = SubjectColor.BrushFor(view.SubjectColorHex);
        KindIcon = CardChoices.IconOf(view.Card.Kind);
        Tags = [.. view.Tags.Select(t => t.DisplayName)];

        HasHint = presenter.HasHint;
        HintText = null;

        CheckMode = presenter.CheckMode;
        TypedAnswer = string.Empty;
        VerdictText = null;
        CanAcceptAsCorrect = false;

        Options.Clear();
        foreach (var (text, index) in presenter.VisibleOptions.Select((t, i) => (t, i)))
        {
            Options.Add(new StudyOptionViewModel(index, text));
        }

        ClozeText = presenter.CheckMode == StudyCheckMode.Cloze ? presenter.VisibleCloze : null;
        HasHiddenGaps = presenter.HasHiddenGaps;

        BuildGrades(view);
        NotifyCommands();
    }

    private void BuildGrades(StudyCardView view)
    {
        Grades.Clear();

        foreach (var grade in Enum.GetValues<ReviewGrade>())
        {
            var preview = view.Previews.FirstOrDefault(p => p.Grade == grade);
            var interval = view.Previews.Count == 0 ? null : FormatInterval(preview.IntervalDays);

            Grades.Add(new StudyGradeViewModel(grade, GradeTitle(grade), interval));
        }
    }

    // ---- действия ------------------------------------------------------------------------------------

    /// <summary>Раскрыть ответ.</summary>
    [RelayCommand(CanExecute = nameof(CanReveal))]
    private void Reveal()
    {
        _presenter!.Reveal();
        ShowAnswer();
    }

    /// <summary>
    /// Клик по wiki-ссылке <c>[[…]]</c> в ответе (new_addons.md §7): переход уводит из сессии — это
    /// осознанное действие пользователя, а не случайность.
    /// </summary>
    private void OnWikiLinkClicked(string target) => _ = ResolveWikiLinkAndNavigateAsync(target);

    private async Task ResolveWikiLinkAndNavigateAsync(string target)
    {
        var resolved = await _cards.ResolveWikiLinkAsync(target).ConfigureAwait(true);
        var id = resolved.ExactCardId ?? (resolved.Candidates.Count == 1 ? resolved.Candidates[0].Id : (Guid?)null);

        if (id is { } cardId)
        {
            _navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(CardId: cardId));
            return;
        }

        _toasts.Show(
            resolved.Candidates.Count > 1 ? "Есть похожие карточки" : "Карточка не найдена",
            resolved.Candidates.Count > 1
                ? $"Точного совпадения нет: {string.Join(", ", resolved.Candidates.Take(3).Select(c => $"«{c.Front}»"))}."
                : $"«{target}» — такой карточки нет.");
    }

    private bool CanReveal() => IsRunning && _presenter is { CanReveal: true };

    private void ShowAnswer()
    {
        var presenter = _presenter!;
        var view = _view!;

        IsRevealed = true;
        AnswerDocument = CardWikiLinkRenderer.Render(_markdown, presenter.VisibleAnswer, OnWikiLinkClicked);
        SourceText = view.Card.Source;
        ClozeText = presenter.CheckMode == StudyCheckMode.Cloze ? presenter.VisibleCloze : null;
        HasHiddenGaps = false;

        Options.Clear();
        foreach (var (text, index) in presenter.VisibleOptions.Select((t, i) => (t, i)))
        {
            Options.Add(new StudyOptionViewModel(index, text) { IsCorrect = index == view.CorrectOptionIndex });
        }

        IntervalHint = BuildIntervalHint(view);
        NotifyCommands();
    }

    /// <summary>Показать подсказку — ответ она не раскрывает.</summary>
    [RelayCommand(CanExecute = nameof(CanShowHintNow))]
    private void ShowHint()
    {
        _presenter!.ShowHint();
        HintText = _presenter.VisibleHint;
        NotifyCommands();
    }

    private bool CanShowHintNow() => IsRunning && _presenter is { HasHint: true, HintUsed: false };

    /// <summary>Раскрыть следующий пропуск.</summary>
    [RelayCommand(CanExecute = nameof(CanRevealGap))]
    private void RevealGap()
    {
        _presenter!.RevealNextGap();
        ClozeText = _presenter.VisibleCloze;
        HasHiddenGaps = _presenter.HasHiddenGaps;

        if (!HasHiddenGaps)
        {
            _presenter.Reveal();
            ShowAnswer();
        }

        NotifyCommands();
    }

    private bool CanRevealGap() => IsRunning && _presenter is { HasHiddenGaps: true };

    /// <summary>Выбрать вариант в тесте — проверка автоматическая.</summary>
    [RelayCommand]
    private async Task ChooseOptionAsync(StudyOptionViewModel? option)
    {
        if (option is null || _presenter is not { CanReveal: true } || _view is null)
        {
            return;
        }

        var correct = option.Index == _view.CorrectOptionIndex;
        option.IsChosen = true;

        _presenter.Reveal();
        ShowAnswer();

        await GradeAsync(correct ? ReviewGrade.Good : ReviewGrade.Again, correct).ConfigureAwait(true);
    }

    /// <summary>Сверить введённый ответ.</summary>
    [RelayCommand]
    private void SubmitTyped()
    {
        if (_presenter is not { CanReveal: true } || _view is null)
        {
            return;
        }

        var match = _sessions.CheckTypedAnswer(_view, TypedAnswer);

        _presenter.Reveal();
        ShowAnswer();

        VerdictText = match.Verdict switch
        {
            AnswerVerdict.Correct => "Верно",
            AnswerVerdict.Close => "Почти верно — сверьте с эталоном",
            _ => "Не то",
        };

        // «Почти верно» решает пользователь: нечёткое сравнение подсказывает, но не судит.
        CanAcceptAsCorrect = match.Verdict != AnswerVerdict.Correct;

        if (match.Verdict == AnswerVerdict.Correct)
        {
            _ = GradeAsync(ReviewGrade.Good, true);
        }

        NotifyCommands();
    }

    /// <summary>«Засчитать» — пользователь считает свой ответ верным.</summary>
    [RelayCommand]
    private Task AcceptAsCorrectAsync() => GradeAsync(ReviewGrade.Hard, true);

    /// <summary>Поставить оценку.</summary>
    [RelayCommand(CanExecute = nameof(CanGrade))]
    private Task Grade(StudyGradeViewModel? grade) =>
        grade is null ? Task.CompletedTask : GradeAsync(grade.Grade, null);

    private bool CanGrade(StudyGradeViewModel? grade) => IsRunning && _presenter is { CanGrade: true };

    /// <summary>Отложить карточку: из тренировок уходит, в поиске остаётся.</summary>
    [RelayCommand(CanExecute = nameof(IsRunningNow))]
    private async Task SuspendAsync()
    {
        if (_view is null)
        {
            return;
        }

        await _cards.SetSuspendedAsync(_view.Card.Id, true).ConfigureAwait(true);
        _messenger.Send(new CardsChangedMessage());

        _order.RemoveAt(_index);
        await ShowCurrentAsync().ConfigureAwait(true);
    }

    /// <summary>Пропустить — карточка уходит в конец и остаётся неотвеченной.</summary>
    [RelayCommand(CanExecute = nameof(IsRunningNow))]
    private async Task SkipAsync()
    {
        if (_plan is null || _order.Count == 0)
        {
            return;
        }

        var cardId = _order[_index];
        _order.RemoveAt(_index);
        _order.Add(cardId);

        await _sessions.SkipAsync(_plan.SessionId, cardId).ConfigureAwait(true);
        await ShowCurrentAsync().ConfigureAwait(true);
    }

    private bool IsRunningNow() => IsRunning;

    /// <summary>Выйти. Незаконченная сессия спрашивает подтверждение.</summary>
    [RelayCommand]
    private async Task ExitAsync()
    {
        if (IsRunning && _index < _order.Count)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Прервать сессию?",
                "Отвеченные карточки уже сохранены — прогресс не пропадёт. Незаконченную сессию можно продолжить позже.",
                "Прервать").ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }
        }

        _timer.Stop();

        if (_plan is not null && !IsFinished)
        {
            await _sessions.FinishAsync(_plan.SessionId).ConfigureAwait(true);
            _messenger.Send(new CardsChangedMessage());
        }

        _navigation.GoBack();
    }

    private async Task GradeAsync(ReviewGrade grade, bool? wasCorrect)
    {
        if (_plan is null || _presenter is null || _view is null || !_presenter.CanGrade)
        {
            return;
        }

        _presenter.MarkGraded();
        NotifyCommands();

        var elapsed = (int)_cardWatch.ElapsedMilliseconds;
        var cardId = _view.Card.Id;

        var result = await _sessions
            .SubmitAnswerAsync(new SubmitAnswerRequest(_plan.SessionId, cardId, grade, elapsed, wasCorrect))
            .ConfigureAwait(true);

        if (result.IsFailure)
        {
            _toasts.Show("Не удалось записать ответ", result.Error.Message, ToastKind.Error);
        }
        else if (result.Value.Outcome.IsRelearning && _options.CurrentValue.RelearnFailedInSameSession)
        {
            // Провал возвращается в эту же сессию — но не следующей же карточкой.
            _prepared.Remove(cardId);
            Requeue(cardId);
        }

        _index++;
        await ShowCurrentAsync().ConfigureAwait(true);
    }

    /// <summary>Вернуть провалившуюся карточку в очередь через несколько показов.</summary>
    private void Requeue(Guid cardId)
    {
        var target = Math.Min(_order.Count, _index + 1 + RelearnGap);
        _order.Insert(target, cardId);
    }

    private async Task FinishAsync()
    {
        if (_plan is null || _finishing)
        {
            return;
        }

        _finishing = true;
        _timer.Stop();

        var result = await _sessions.FinishAsync(_plan.SessionId).ConfigureAwait(true);
        _messenger.Send(new CardsChangedMessage());

        if (result.IsFailure)
        {
            EmptyMessage = result.Error.Message;
            Phase = StudySessionPhase.Finished;
            return;
        }

        ApplySummary(result.Value);
        Phase = StudySessionPhase.Finished;
    }

    private void ApplySummary(StudySessionSummary summary)
    {
        Summary = summary;

        ScoreText = $"{summary.Correct} / {summary.Answered}";
        AccuracyText = $"{summary.Accuracy * 100:0} %";
        ElapsedText = summary.Elapsed.TotalHours >= 1
            ? $"{(int)summary.Elapsed.TotalHours} ч {summary.Elapsed.Minutes} мин"
            : $"{(int)summary.Elapsed.TotalMinutes} мин {summary.Elapsed.Seconds} с";

        SubjectScores.Clear();
        foreach (var row in summary.BySubject)
        {
            SubjectScores.Add(new StudyScoreRowViewModel(row.SubjectName, row.Answered, row.Correct));
        }

        DeckScores.Clear();
        foreach (var row in summary.ByDeck)
        {
            DeckScores.Add(new StudyScoreRowViewModel(row.DeckName, row.Answered, row.Correct));
        }

        WeakTags.Clear();
        foreach (var row in summary.WeakTags)
        {
            WeakTags.Add(new StudyScoreRowViewModel(row.TagName, row.Answered, row.Correct));
        }

        Missed.Clear();
        foreach (var miss in summary.Missed)
        {
            Missed.Add(new StudyMissViewModel(miss.CardId, miss.Front, miss.Back));
        }
    }

    // ---- итоговые действия --------------------------------------------------------------------------------

    /// <summary>Работа над ошибками — сразу новая сессия из промахов.</summary>
    [RelayCommand(CanExecute = nameof(HasMisses))]
    private async Task WorkOnMistakesAsync()
    {
        var result = await _sessions.StartMistakesAsync(Summary!.SessionId).ConfigureAwait(true);

        if (result.IsFailure)
        {
            _toasts.Show("Работа над ошибками", result.Error.Message, ToastKind.Warning);
            return;
        }

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            StudyMode.Mistakes,
            StudySessionFilter.Empty with
            {
                CardIds = [.. Summary.Missed.Select(m => m.CardId)],
                Order = StudyOrder.HardestFirst,
            },
            Title: "Работа над ошибками"));
    }

    private bool HasMisses() => Summary is { Missed.Count: > 0 };

    /// <summary>«Ещё раз то же самое» — тот же фильтр и то же зерно.</summary>
    [RelayCommand(CanExecute = nameof(HasSummary))]
    private void RepeatSame()
    {
        if (_plan is null)
        {
            return;
        }

        _navigation.NavigateTo<StudySessionViewModel>(new StudySessionParameter(
            _plan.Mode,
            _plan.Filter,
            _plan.Seed,
            Title));
    }

    private bool HasSummary() => Summary is not null;

    /// <summary>Собрать промахи в отдельную колоду, чтобы вернуться к ним позже.</summary>
    [RelayCommand(CanExecute = nameof(HasMisses))]
    private async Task CollectMissesAsync()
    {
        var name = $"Ошибки · {DateTime.Today:dd.MM.yyyy}";
        var ids = Summary!.Missed.Select(m => m.CardId).ToList();

        var result = await _cards.AssignDeckAsync(ids, await EnsureDeckAsync(name).ConfigureAwait(true))
            .ConfigureAwait(true);

        if (result.IsSuccess)
        {
            _toasts.Show("Колода собрана", $"«{name}»: {ids.Count}.", ToastKind.Success);
            _messenger.Send(new CardsChangedMessage());
        }
        else
        {
            _toasts.Show("Не удалось собрать колоду", result.Error.Message, ToastKind.Error);
        }
    }

    private async Task<Guid?> EnsureDeckAsync(string name)
    {
        var decks = await _decks.GetAllAsync().ConfigureAwait(true);
        var existing = decks.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return existing.Id;
        }

        var created = await _decks.CreateAsync(new CardDeck { Name = name }).ConfigureAwait(true);
        return created.IsSuccess ? created.Value : null;
    }

    // ---- вспомогательное ------------------------------------------------------------------------------------

    private void UpdateProgress()
    {
        var total = Math.Max(1, _order.Count);
        ProgressText = $"{Math.Min(_index + 1, total)} / {total}";
        ProgressValue = (double)_index / total;
    }

    private void UpdateTimer()
    {
        if (_plan is null)
        {
            return;
        }

        if (_plan.TimeLimitSeconds is { } limit)
        {
            var left = TimeSpan.FromSeconds(limit) - (DateTimeOffset.Now - _startedAt);

            if (left <= TimeSpan.Zero)
            {
                TimerText = "00:00";
                IsTimeWarning = true;
                _ = FinishAsync();
                return;
            }

            TimerText = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
            IsTimeWarning = left.TotalSeconds <= TimeWarningSeconds;
            return;
        }

        if (_plan.PerCardLimitSeconds is { } perCard)
        {
            var left = TimeSpan.FromSeconds(perCard) - _cardWatch.Elapsed;

            if (left <= TimeSpan.Zero)
            {
                _ = SkipAsync();
                return;
            }

            TimerText = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
            IsTimeWarning = left.TotalSeconds <= 10;
        }
    }

    private string? BuildIntervalHint(StudyCardView view)
    {
        if (view.Previews.Count == 0)
        {
            return null;
        }

        var before = view.Card.IntervalDays;
        return before <= 0
            ? "Новая карточка"
            : $"Интервал был {FormatInterval(before)}";
    }

    private void NotifyCommands()
    {
        RevealCommand.NotifyCanExecuteChanged();
        ShowHintCommand.NotifyCanExecuteChanged();
        RevealGapCommand.NotifyCanExecuteChanged();
        GradeCommand.NotifyCanExecuteChanged();
        SuspendCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        WorkOnMistakesCommand.NotifyCanExecuteChanged();
        RepeatSameCommand.NotifyCanExecuteChanged();
        CollectMissesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowHint));
    }

    private static string TitleFor(StudyMode mode) => mode switch
    {
        StudyMode.Review => "Повторение",
        StudyMode.Exam => "Пробный экзамен",
        StudyMode.Mistakes => "Работа над ошибками",
        StudyMode.Cram => "Аврал",
        _ => "Тренировка",
    };

    private static string GradeTitle(ReviewGrade grade) => grade switch
    {
        ReviewGrade.Again => "Не помню",
        ReviewGrade.Hard => "Трудно",
        ReviewGrade.Good => "Хорошо",
        _ => "Легко",
    };

    /// <summary>Человеческая подпись интервала: минуты, дни, месяцы.</summary>
    internal static string FormatInterval(double days) => days switch
    {
        < 1.0 / 24 => $"{Math.Max(1, (int)Math.Round(days * 1440))} мин",
        < 1 => $"{Math.Max(1, (int)Math.Round(days * 24))} ч",
        < 30 => $"{Math.Round(days, 0)} дн",
        < 365 => $"{Math.Round(days / 30, 1)} мес",
        _ => $"{Math.Round(days / 365, 1)} г",
    };

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Stop();
        _cardWatch.Stop();
    }
}

/// <summary>Вариант ответа в тесте.</summary>
public sealed partial class StudyOptionViewModel(int index, string text) : ObservableObject
{
    public int Index { get; } = index;

    public string Text { get; } = text;

    /// <summary>Пользователь выбрал этот вариант.</summary>
    [ObservableProperty]
    private bool _isChosen;

    /// <summary>Вариант верный — подсвечивается после раскрытия.</summary>
    [ObservableProperty]
    private bool _isCorrect;
}

/// <summary>Кнопка оценки с подписью будущего интервала («Хорошо · через 10 дней»).</summary>
public sealed class StudyGradeViewModel(ReviewGrade grade, string title, string? interval)
{
    public ReviewGrade Grade { get; } = grade;

    public string Title { get; } = title;

    /// <summary>Каким станет интервал; пусто, если подписи выключены в настройках.</summary>
    public string? Interval { get; } = interval;

    public bool HasInterval => !string.IsNullOrEmpty(Interval);
}

/// <summary>Строка разбивки итогов: предмет, колода или метка.</summary>
public sealed class StudyScoreRowViewModel(string name, int answered, int correct)
{
    public string Name { get; } = name;

    public int Answered { get; } = answered;

    public int Correct { get; } = correct;

    public double Ratio => Answered == 0 ? 0 : (double)Correct / Answered;

    public string Text => $"{Correct} / {Answered} · {Ratio * 100:0} %";
}

/// <summary>Промах в списке ошибок — ответ раскрывается по кнопке.</summary>
public sealed partial class StudyMissViewModel(Guid cardId, string front, string back) : ObservableObject
{
    public Guid CardId { get; } = cardId;

    public string Front { get; } = front;

    public string Back { get; } = back;

    [ObservableProperty]
    private bool _isAnswerVisible;

    [RelayCommand]
    private void ToggleAnswer() => IsAnswerVisible = !IsAnswerVisible;
}
