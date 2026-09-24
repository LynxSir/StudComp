using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Controls;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Cards.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels.Cards;

namespace StudComp.ViewModels.Organizer.Hub;

/// <summary>Строка списка заметок предмета.</summary>
public sealed class NoteRowViewModel(Note note)
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public Note Note { get; } = note;

    public Guid Id => Note.Id;

    public string Title => Note.Title;

    public bool IsPinned => Note.IsPinned;

    public string UpdatedText { get; } = note.UpdatedAt.LocalDateTime.ToString("dd.MM HH:mm", Ru);

    public string KindText { get; } = note.Kind switch
    {
        NoteKind.Lecture => "лекция",
        NoteKind.FileNote => "к файлу",
        NoteKind.FolderNote => "к папке",
        _ => "заметка",
    };

    /// <summary>Первая строка текста — превью в списке.</summary>
    public string Excerpt { get; } = BuildExcerpt(note.ContentMarkdown);

    private static string BuildExcerpt(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "пусто";
        }

        var line = markdown
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('#', '>', '-', '*', ' '))
            .FirstOrDefault(x => x.Length > 0) ?? "пусто";

        return line.Length > 120 ? line[..120] + "…" : line;
    }
}

/// <summary>
/// Вкладка «Заметки» Хаба предмета (new_addons.md §5): список заметок слева, редактор справа.
/// </summary>
public sealed partial class HubNotesViewModel(
    INoteService notes,
    NoteEditorViewModel editor,
    IDialogService dialogs,
    IToastService toasts,
    ICardService cards,
    ICardDeckService cardDecks,
    ICardTagService cardTags,
    ISubjectService subjects,
    INoteToCardsService noteToCards) : ObservableObject
{
    private Guid _subjectId;

    public NoteEditorViewModel Editor { get; } = editor;

    public ObservableCollection<NoteRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private NoteRowViewModel? _selectedNote;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasItems => Items.Count > 0;

    partial void OnSelectedNoteChanged(NoteRowViewModel? value)
    {
        if (value is not null)
        {
            _ = Editor.LoadAsync(value.Id);
        }
    }

    public async Task LoadAsync(Guid subjectId, Guid? openNoteId = null)
    {
        _subjectId = subjectId;

        // Список перечитывается после каждого автосохранения — иначе заголовок в списке отстаёт.
        Editor.Saved -= OnEditorSaved;
        Editor.Saved += OnEditorSaved;

        Editor.CardFromSelectionRequested -= OnCardFromSelectionRequested;
        Editor.CardFromSelectionRequested += OnCardFromSelectionRequested;

        await RefreshAsync().ConfigureAwait(true);

        if (openNoteId is { } noteId && Items.FirstOrDefault(x => x.Id == noteId) is { } row)
        {
            SelectedNote = row;
        }
    }

    /// <summary>Дописать несохранённое — зовётся при уходе со страницы Хаба.</summary>
    public Task FlushAsync() => Editor.FlushAsync();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_subjectId == Guid.Empty)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var keepId = SelectedNote?.Id;
            var list = await notes.GetBySubjectAsync(_subjectId).ConfigureAwait(true);

            Items.Clear();
            foreach (var note in list)
            {
                Items.Add(new NoteRowViewModel(note));
            }

            OnPropertyChanged(nameof(HasItems));
            SelectedNote = Items.FirstOrDefault(x => x.Id == keepId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var result = await notes.CreateAsync(new Note
        {
            SubjectId = _subjectId,
            Kind = NoteKind.Lecture,
            Title = "Новая заметка",
            ContentMarkdown = string.Empty,
        });

        if (result.IsFailure)
        {
            toasts.Show("Не удалось создать заметку", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshAsync();
        SelectedNote = Items.FirstOrDefault(x => x.Id == result.Value);
    }

    [RelayCommand]
    private async Task TogglePinAsync(NoteRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var result = await notes.SetPinnedAsync(row.Id, !row.IsPinned);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось закрепить заметку", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(NoteRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "Удалить заметку?", $"«{row.Title}» будет удалена безвозвратно.");
        if (!confirmed)
        {
            return;
        }

        if (SelectedNote?.Id == row.Id)
        {
            await Editor.CloseAsync();
        }

        var result = await notes.DeleteAsync(row.Id);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось удалить заметку", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshAsync();
    }

    private void OnEditorSaved(object? sender, EventArgs e) => _ = RefreshAsync();

    /// <summary>«Сделать карточку» на выделении (new_addons.md §7.2) — открывает форму, ничего не создавая молча.</summary>
    private void OnCardFromSelectionRequested(object? sender, CardFromSelectionRequestedEventArgs e) =>
        _ = CreateCardFromSelectionAsync(e);

    private async Task CreateCardFromSelectionAsync(CardFromSelectionRequestedEventArgs request)
    {
        var allSubjects = await subjects.GetAllAsync().ConfigureAwait(true);
        var allDecks = await cardDecks.GetAllAsync().ConfigureAwait(true);
        var allTags = await cardTags.GetAllAsync().ConfigureAwait(true);

        var editor = new CardEditorViewModel(
            null,
            allSubjects,
            allDecks,
            allTags,
            cards,
            preselectedSubjectId: request.SubjectId,
            prefilledFront: request.Front,
            prefilledBack: request.Back,
            sourceNoteId: request.NoteId,
            dialogs: dialogs);

        if (!await dialogs.ShowEditorAsync(editor, editor.HeaderText).ConfigureAwait(true))
        {
            return;
        }

        var result = await cards.CreateAsync(editor.ToModel(), editor.ParseTags()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось создать карточку", result.Error.Message, ToastKind.Error);
            return;
        }

        toasts.Show("Карточка создана", $"«{request.Front}» — в Картотеке.");
    }

    /// <summary>«Разобрать на карточки» (new_addons.md §7.3) — предпросмотр, ничего не создаётся молча.</summary>
    [RelayCommand]
    private async Task ParseIntoCardsAsync()
    {
        if (SelectedNote is not { } note)
        {
            return;
        }

        var candidates = await noteToCards.BuildCandidatesAsync(note.Id, Editor.Content).ConfigureAwait(true);
        if (candidates.Count == 0)
        {
            toasts.Show("Разбирать нечего", "Не нашлось узнаваемых терминов, вопросов или списков.");
            return;
        }

        var preview = new NoteCardsPreviewViewModel(candidates);
        if (!await dialogs.ShowEditorAsync(preview, "Разобрать на карточки", "Создать").ConfigureAwait(true))
        {
            return;
        }

        var accepted = preview.SelectedResults();
        if (accepted.Count == 0)
        {
            return;
        }

        var created = await noteToCards.CreateFromCandidatesAsync(note.Id, _subjectId, accepted).ConfigureAwait(true);
        toasts.Show("Карточки созданы", $"Заведено карточек: {created}.");
    }
}
