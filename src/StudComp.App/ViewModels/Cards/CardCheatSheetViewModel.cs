using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Компактный режим-шпаргалка (new_addons.md §8.6): только строка поиска и результаты с раскрытием
/// ответа — картотека как справочник, пока пользователь работает в другом окне.
/// </summary>
/// <remarks>
/// Переиспользует строительные блоки <see cref="CardPaletteViewModel"/> (<see cref="CardQuery"/>,
/// подсветку, тот же дебаунс 150 мс) и саму строку выдачи <see cref="CardPaletteRowViewModel"/>, но не
/// сам оверлей целиком: у него другая семантика (<c>IsOpen</c> в главном окне) и другое время жизни —
/// компактное окно живёт весь сеанс работы, храня последний запрос между показами, тогда как оверлей
/// пересоздаёт себя при каждом <c>Ctrl+K</c>. Навигации в раздел здесь нет намеренно: окно вызывают,
/// когда работают в стороннем приложении, и переключаться в Rubrica незачем.
/// </remarks>
public sealed partial class CardCheatSheetViewModel : ObservableObject
{
    private const int SearchDebounceMs = 150;
    private const int MaxResults = 8;

    private readonly ICardService _cards;
    private readonly ISubjectService _subjects;

    private CancellationTokenSource? _searchCts;
    private IReadOnlyList<Subject> _subjectList = [];

    public CardCheatSheetViewModel(ICardService cards, ISubjectService subjects)
    {
        _cards = cards;
        _subjects = subjects;
    }

    public ObservableCollection<CardPaletteRowViewModel> Results { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private CardPaletteRowViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHint))]
    private bool _hasResults;

    public bool ShowHint => !HasResults;

    [ObservableProperty]
    private string _hintText = "Начните печатать — найдётся по обеим сторонам карточки и по меткам.";

    /// <summary>Показано окно — подгрузить предметы и повторить последний запрос, если он есть.</summary>
    public void Activate()
    {
        _ = LoadSubjectsAsync();
        if (Query.Length > 0)
        {
            ScheduleSearch();
        }
    }

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
