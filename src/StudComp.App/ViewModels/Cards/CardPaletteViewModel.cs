using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Оверлей глобального поиска по картотеке — <c>Ctrl+K</c> с любого экрана (new_addons.md §4.6).
/// </summary>
/// <remarks>
/// Главный ответ на «быстренько найти»: не уходя со своего экрана, набрать слово, стрелками выбрать
/// карточку и раскрыть ответ прямо здесь. <c>Enter</c> открывает её в разделе — это уже переход, и
/// он делается осознанно.
/// </remarks>
public sealed partial class CardPaletteViewModel : ObservableObject
{
    /// <summary>Дебаунс — тот же, что в библиотеке.</summary>
    private const int SearchDebounceMs = 150;

    /// <summary>Больше восьми строк в оверлее читать невозможно.</summary>
    private const int MaxResults = 8;

    private readonly ICardService _cards;
    private readonly ISubjectService _subjects;
    private readonly INavigationService _navigation;

    private CancellationTokenSource? _searchCts;
    private IReadOnlyList<Subject> _subjectList = [];

    public CardPaletteViewModel(
        ICardService cards,
        ISubjectService subjects,
        INavigationService navigation)
    {
        _cards = cards;
        _subjects = subjects;
        _navigation = navigation;
    }

    /// <summary>Результаты поиска — до восьми строк.</summary>
    public ObservableCollection<CardPaletteRowViewModel> Results { get; } = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private CardPaletteRowViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHint))]
    private bool _hasResults;

    /// <summary>Пока ничего не найдено, оверлей объясняет, что здесь вообще происходит.</summary>
    public bool ShowHint => !HasResults;

    [ObservableProperty]
    private string _hintText = "Начните печатать — найдётся по обеим сторонам карточки и по меткам.";

    /// <summary>Открыть оверлей. Предыдущий запрос сохраняется — обычно ищут то же самое.</summary>
    [RelayCommand]
    public void Open()
    {
        UiActivity.Mark("Палитра Ctrl+K → открытие");
        IsOpen = true;
        _ = LoadSubjectsAsync();
        ScheduleSearch();
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        _searchCts?.Cancel();
    }

    /// <summary>Открыть выбранную карточку в разделе «Картотека».</summary>
    [RelayCommand]
    private void OpenSelected()
    {
        if (Selected is not { } row)
        {
            return;
        }

        Close();
        _navigation.NavigateTo<CardsPageViewModel>(new CardsPageParameter(row.Id));
    }

    /// <summary>Раскрыть ответ, не покидая текущий экран, — <c>Space</c>.</summary>
    [RelayCommand]
    private void ToggleAnswer()
    {
        if (Selected is { } row)
        {
            row.IsAnswerVisible = !row.IsAnswerVisible;
        }
    }

    [RelayCommand]
    private void SelectNext() => Move(1);

    [RelayCommand]
    private void SelectPrevious() => Move(-1);

    partial void OnQueryChanged(string value) => ScheduleSearch();

    private void Move(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[Math.Clamp(index + delta, 0, Results.Count - 1)];
    }

    private async Task LoadSubjectsAsync()
    {
        if (_subjectList.Count == 0)
        {
            _subjectList = await _subjects.GetAllAsync().ConfigureAwait(true);
        }
    }

    private void ScheduleSearch()
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = DebouncedSearchAsync(cts.Token);
    }

    private async Task DebouncedSearchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, ct).ConfigureAwait(true);
            await SearchAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Заменён более свежим вводом.
        }
    }

    private async Task SearchAsync(CancellationToken ct)
    {
        var spec = CardQuery.Parse(Query);
        var terms = spec.Terms;

        try
        {
            var found = await _cards
                .SearchDetailedAsync(Query, new CardSearchOptions(Limit: MaxResults), ct)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            Results.Clear();
            foreach (var result in found)
            {
                var subject = result.Card.SubjectId is { } id
                    ? _subjectList.FirstOrDefault(x => x.Id == id)
                    : null;

                Results.Add(new CardPaletteRowViewModel(result.Card, subject?.Name, result.Snippet, terms));
            }

            HasResults = Results.Count > 0;
            Selected = Results.FirstOrDefault();
            HintText = Results.Count == 0 && Query.Trim().Length > 0
                ? $"Ничего не нашлось по запросу «{Query.Trim()}»."
                : "Начните печатать — найдётся по обеим сторонам карточки и по меткам.";
        }
        catch (OperationCanceledException)
        {
            // Заменён более свежим запросом.
        }
        catch (Exception)
        {
            HintText = "Не удалось выполнить поиск.";
            HasResults = false;
        }
    }
}

/// <summary>Строка оверлея: лицевая сторона всегда, оборот — по запросу.</summary>
public sealed partial class CardPaletteRowViewModel : ObservableObject
{
    /// <summary>Оборот в оверлее показывается коротко: это подсказка, а не чтение.</summary>
    private const int AnswerLength = 240;

    public CardPaletteRowViewModel(
        Card card,
        string? subjectName,
        string snippet,
        IReadOnlyList<CardQueryTerm> terms)
    {
        Id = card.Id;
        SubjectName = subjectName ?? string.Empty;
        FrontSegments = CardHighlight.FromTerms(card.Front, terms);

        var flat = string.Join(
            ' ',
            card.Back.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        BackSegments = snippet.Length > 0
            ? CardHighlight.FromSnippet(snippet)
            : CardHighlight.FromTerms(flat, terms, AnswerLength);
    }

    public Guid Id { get; }

    public string SubjectName { get; }

    public bool HasSubject => SubjectName.Length > 0;

    public IReadOnlyList<CardTextSegment> FrontSegments { get; }

    public IReadOnlyList<CardTextSegment> BackSegments { get; }

    /// <summary>Раскрыт ли оборот — <c>Space</c> переключает его у выбранной строки.</summary>
    [ObservableProperty]
    private bool _isAnswerVisible;
}
