using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;
using StudComp.ViewModels.Cards;

namespace StudComp.ViewModels.Organizer.Hub;

/// <summary>Одна строка предпросмотра разбора заметки — с чекбоксом и правкой на месте (new_addons.md §7.3).</summary>
public sealed partial class NoteCardCandidateRowViewModel : ObservableObject
{
    public NoteCardCandidateRowViewModel(NoteCardCandidateResult candidate)
    {
        Kind = candidate.Kind;
        IsLikelyDuplicate = candidate.IsLikelyDuplicate;
        _front = candidate.Front;
        _back = candidate.Back;

        // Уже есть карточка из этой же заметки — чекбокс приходит снятым, повторный разбор не плодит дубли.
        _isSelected = !candidate.IsLikelyDuplicate;
    }

    public CardKind Kind { get; }

    public bool IsLikelyDuplicate { get; }

    public string KindText => CardChoices.DisplayOf(Kind);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _front;

    [ObservableProperty]
    private string _back;

    public NoteCardCandidateResult ToResult() => new(Front, Back, Kind, IsLikelyDuplicate: false, ExistingCardId: null);
}

/// <summary>
/// Диалог предпросмотра «Разобрать на карточки» (new_addons.md §7.3): список кандидатов с
/// чекбоксами, ничего не создаётся молча.
/// </summary>
public sealed class NoteCardsPreviewViewModel
{
    public NoteCardsPreviewViewModel(IReadOnlyList<NoteCardCandidateResult> candidates)
    {
        Rows = candidates.Select(c => new NoteCardCandidateRowViewModel(c)).ToList();
    }

    public IReadOnlyList<NoteCardCandidateRowViewModel> Rows { get; }

    /// <summary>Кнопка диалога активна всегда — пользователь может снять все чекбоксы и просто закрыть.</summary>
    public bool CanSave => true;

    public IReadOnlyList<NoteCardCandidateResult> SelectedResults() =>
        Rows.Where(r => r.IsSelected).Select(r => r.ToResult()).ToList();
}
