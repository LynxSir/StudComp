using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Cards;

/// <summary>
/// Форма колоды (new_addons.md §3.1). Заполненный запрос превращает обычную колоду в «умную
/// подборку»: её содержимое вычисляется на лету, а не хранится ссылками.
/// </summary>
public sealed partial class CardDeckEditorViewModel : ObservableObject
{
    /// <summary>Псевдо-предмет «без предмета» — тот же приём, что в CardEditorViewModel/
    /// RuleEditorViewModel.AnySubject (new_addons.md §9.1).</summary>
    private static readonly Subject AnySubject = new() { Id = Guid.Empty, Name = "— без предмета —" };

    private readonly CardDeck _deck;

    public CardDeckEditorViewModel(CardDeck? deck, IReadOnlyList<Subject> subjects)
    {
        IsEditMode = deck is not null;
        _deck = deck ?? new CardDeck { Id = Guid.NewGuid() };

        Subjects = [AnySubject, .. subjects];

        _name = _deck.Name;
        _description = _deck.Description ?? string.Empty;
        _queryExpression = _deck.QueryExpression ?? string.Empty;
        _selectedSubject = Subjects.FirstOrDefault(x => x.Id == _deck.SubjectId) ?? AnySubject;
    }

    public bool IsEditMode { get; }

    public string HeaderText => IsEditMode ? "Изменить колоду" : "Новая колода";

    /// <summary>Предметы плюс пункт «без предмета» первым.</summary>
    public IReadOnlyList<Subject> Subjects { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSmartDeck))]
    private string _queryExpression;

    [ObservableProperty]
    private Subject _selectedSubject;

    /// <summary>Заполнен запрос — колода вычисляемая.</summary>
    public bool IsSmartDeck => !string.IsNullOrWhiteSpace(QueryExpression);

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    public CardDeck ToModel()
    {
        _deck.Name = Name.Trim();
        _deck.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        _deck.QueryExpression = string.IsNullOrWhiteSpace(QueryExpression) ? null : QueryExpression.Trim();
        _deck.SubjectId = SelectedSubject.Id == Guid.Empty ? null : SelectedSubject.Id;
        return _deck;
    }
}
