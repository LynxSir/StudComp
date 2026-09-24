using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;
using StudComp.Services;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Форма карточки (new_addons.md §7.1). Создаётся вручную в списочной VM — ей нужны данные времени
/// выполнения (списки предметов и колод, редактируемая карточка), поэтому в DI её нет (ADR §16.24).
/// </summary>
public sealed partial class CardEditorViewModel : ObservableObject
{
    /// <summary>Псевдо-предмет «без предмета» — приём из RuleEditorViewModel.AnySubject (Phase 8):
    /// подставной объект с читаемым Name работает с DisplayMemberPath без конвертера на null.</summary>
    private static readonly Subject AnySubject = new() { Id = Guid.Empty, Name = "— без предмета —" };

    /// <summary>Псевдо-колода «без колоды» — тот же приём (new_addons.md §9.1).</summary>
    private static readonly CardDeck AnyDeck = new() { Id = Guid.Empty, Name = "— без колоды —" };

    private readonly Card _card;
    private readonly ICardService _cards;
    private readonly IReadOnlyList<CardDeck> _allDecks;

    /// <summary>Нужен только для шпаргалки по разметке; форма создаётся вручную, поэтому необязателен
    /// (приём из SubjectEditorViewModel, Phase 13.2).</summary>
    private readonly IDialogService? _dialogs;

    private CancellationTokenSource? _duplicateCts;

    public CardEditorViewModel(
        Card? card,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<CardDeck> decks,
        IReadOnlyList<CardTag> existingTags,
        ICardService cards,
        Guid? preselectedSubjectId = null,
        string? prefilledFront = null,
        string? prefilledBack = null,
        Guid? sourceNoteId = null,
        IDialogService? dialogs = null)
    {
        _cards = cards;
        _dialogs = dialogs;
        _allDecks = decks;
        IsEditMode = card is not null;
        _card = card ?? new Card { Id = Guid.NewGuid(), Kind = CardKind.Term, SourceNoteId = sourceNoteId };

        Subjects = [AnySubject, .. subjects];
        TagSuggestions = existingTags.Select(x => x.DisplayName).ToArray();

        // Предзаполнение — только для новой карточки: у существующей есть своё содержимое.
        if (card is null)
        {
            _card.Front = prefilledFront ?? string.Empty;
            _card.Back = prefilledBack ?? string.Empty;
        }

        _front = _card.Front;
        _back = _card.Back;
        _hint = _card.Hint ?? string.Empty;
        _source = _card.Source ?? string.Empty;
        _isPinned = _card.IsPinned;
        _isSuspended = _card.IsSuspended;

        var subjectId = _card.SubjectId ?? preselectedSubjectId;
        _selectedSubject = Subjects.FirstOrDefault(x => x.Id == subjectId) ?? AnySubject;
        _decks = BuildDeckChoices(_selectedSubject);
        _selectedDeck = _decks.FirstOrDefault(x => x.Id == _card.DeckId) ?? AnyDeck;
        _selectedKind = CardChoices.Kinds.First(x => x.Value == _card.Kind);
        _selectedDifficulty = CardChoices.Difficulties.First(x => x.Value == _card.Difficulty);
        _tagsText = string.Empty;
    }

    public bool IsEditMode { get; }

    public string HeaderText => IsEditMode ? "Изменить карточку" : "Новая карточка";

    /// <summary>Показывать ли кнопку справки: без диалогов открывать её нечем.</summary>
    public bool CanShowMarkdownHelp => _dialogs is not null;

    /// <summary>Шпаргалка по разметке (new_addons.md §11.4): оборот карточки — тоже Markdown.</summary>
    [RelayCommand]
    private Task ShowMarkdownHelpAsync() =>
        _dialogs?.ShowInfoAsync(new MarkdownHelpViewModel(), "Разметка Markdown") ?? Task.CompletedTask;

    /// <summary>Предметы плюс пункт «без предмета» первым.</summary>
    public IReadOnlyList<Subject> Subjects { get; }

    public IReadOnlyList<string> TagSuggestions { get; }

    public IReadOnlyList<NamedChoice<CardKind>> Kinds => CardChoices.Kinds;

    public IReadOnlyList<NamedChoice<CardDifficulty>> Difficulties => CardChoices.Difficulties;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _front;

    [ObservableProperty]
    private string _back;

    [ObservableProperty]
    private string _hint;

    [ObservableProperty]
    private string _source;

    /// <summary>Метки через запятую — так их быстрее всего набирать с клавиатуры.</summary>
    [ObservableProperty]
    private string _tagsText;

    /// <summary>Колоды, отфильтрованные под выбранный предмет, плюс пункт «без колоды» первым
    /// (new_addons.md §9.1 — раньше список был не отфильтрован и открывался пустой строкой).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRealDeckChoices))]
    private IReadOnlyList<CardDeck> _decks;

    [ObservableProperty]
    private Subject _selectedSubject;

    [ObservableProperty]
    private CardDeck _selectedDeck;

    [ObservableProperty]
    private NamedChoice<CardKind> _selectedKind;

    [ObservableProperty]
    private NamedChoice<CardDifficulty> _selectedDifficulty;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isSuspended;

    /// <summary>Текст неблокирующей плашки «похожая карточка уже есть»; пусто — плашки нет.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuplicateWarning))]
    private string _duplicateWarning = string.Empty;

    public bool HasDuplicateWarning => DuplicateWarning.Length > 0;

    /// <summary>Есть хоть одна настоящая колода среди вариантов (сверх «без колоды») — иначе
    /// комбобокс дизейблится, а не открывается пустым (new_addons.md §9.1).</summary>
    public bool HasRealDeckChoices => Decks.Count > 1;

    public bool CanSave => !string.IsNullOrWhiteSpace(Front);

    /// <summary>Колоды, доступные выбранному предмету: свои плюс общие (без предмета) — выбор
    /// предмета не должен молча отбирать уже существующую валидную колоду.</summary>
    private IReadOnlyList<CardDeck> BuildDeckChoices(Subject subject) =>
        subject.Id == Guid.Empty
            ? [AnyDeck, .. _allDecks]
            : [AnyDeck, .. _allDecks.Where(d => d.SubjectId is null || d.SubjectId == subject.Id)];

    partial void OnSelectedSubjectChanged(Subject value)
    {
        Decks = BuildDeckChoices(value);
        SelectedDeck = Decks.FirstOrDefault(d => d.Id == SelectedDeck.Id) ?? AnyDeck;
    }

    /// <summary>Загрузить метки редактируемой карточки — их приходится читать отдельным запросом.</summary>
    public void SetTags(IReadOnlyList<CardTag> tags) =>
        TagsText = string.Join(", ", tags.Select(x => x.DisplayName));

    /// <summary>Разобрать поле меток. Пустые куски и повторы отсеет сервис меток.</summary>
    public IReadOnlyList<string> ParseTags() =>
        TagsText.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public Card ToModel()
    {
        _card.Front = Front.Trim();
        _card.Back = Back;
        _card.Hint = string.IsNullOrWhiteSpace(Hint) ? null : Hint.Trim();
        _card.Source = string.IsNullOrWhiteSpace(Source) ? null : Source.Trim();
        _card.SubjectId = SelectedSubject.Id == Guid.Empty ? null : SelectedSubject.Id;
        _card.DeckId = SelectedDeck.Id == Guid.Empty ? null : SelectedDeck.Id;
        _card.Kind = SelectedKind.Value;
        _card.Difficulty = SelectedDifficulty.Value;
        _card.IsPinned = IsPinned;
        _card.IsSuspended = IsSuspended;
        return _card;
    }

    partial void OnFrontChanged(string value) => ScheduleDuplicateCheck();

    /// <summary>
    /// Проверка на дубликат — тем же дебаунсом с отменой предыдущего запроса, что живой предпросмотр
    /// правила Архивариуса. Плашка неблокирующая: создать похожую карточку никто не мешает.
    /// </summary>
    private void ScheduleDuplicateCheck()
    {
        _duplicateCts?.Cancel();
        var cts = new CancellationTokenSource();
        _duplicateCts = cts;
        _ = CheckDuplicatesAsync(cts.Token);
    }

    private async Task CheckDuplicatesAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct).ConfigureAwait(true);

            var front = Front.Trim();
            if (front.Length < 2)
            {
                DuplicateWarning = string.Empty;
                return;
            }

            var similar = await _cards
                .FindSimilarAsync(front, IsEditMode ? _card.Id : null, ct)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            DuplicateWarning = similar.Count == 0
                ? string.Empty
                : $"Похожая карточка уже есть: «{similar[0].Front}»";
        }
        catch (OperationCanceledException)
        {
            // заменена более свежей проверкой
        }
        catch (Exception)
        {
            // Подсказка о дубликате — приятная мелочь, а не повод мешать заполнять форму.
            DuplicateWarning = string.Empty;
        }
    }
}
