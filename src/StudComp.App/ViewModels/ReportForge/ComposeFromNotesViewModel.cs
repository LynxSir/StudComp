using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.Modules.Organizer.Services;

namespace StudComp.ViewModels.ReportForge;

/// <summary>Строка заметки в диалоге «Собрать из заметок» — с чекбоксом выбора.</summary>
public sealed partial class NoteSelectionRowViewModel : ObservableObject
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly Note _note;
    private readonly Action _selectionChanged;

    public NoteSelectionRowViewModel(Note note, Action selectionChanged)
    {
        _note = note;
        _selectionChanged = selectionChanged;
        Title = note.Title;
        Excerpt = BuildExcerpt(note.ContentMarkdown);
        UpdatedText = note.UpdatedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm", Russian);
    }

    public Guid Id => _note.Id;

    public string Title { get; }

    public string Excerpt { get; }

    public string UpdatedText { get; }

    public string ContentMarkdown => _note.ContentMarkdown;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();

    /// <summary>Та же эвристика превью, что у <c>NoteRowViewModel</c> в Хабе предмета.</summary>
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
/// Диалог «Собрать из заметок» (new_addons.md §6): выбрать предмет → мультивыбор его заметок →
/// склейка в markdown-редактор «Нового отчёта» с заголовками. Список предметов передаётся уже
/// загруженным из <see cref="NewReportViewModel"/> — переспрашивать <c>ISubjectService</c> незачем.
/// </summary>
public sealed partial class ComposeFromNotesViewModel : ObservableObject
{
    private readonly INoteService _notes;

    public ComposeFromNotesViewModel(IReadOnlyList<Subject> subjects, INoteService notes, Guid? preselectedSubjectId)
    {
        _notes = notes;
        Subjects = subjects;
        _selectedSubject = subjects.FirstOrDefault(s => s.Id == preselectedSubjectId) ?? subjects.FirstOrDefault();
        _ = LoadNotesAsync();
    }

    public IReadOnlyList<Subject> Subjects { get; }

    public ObservableCollection<NoteSelectionRowViewModel> Notes { get; } = [];

    [ObservableProperty]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canSave;

    public bool HasNotes => Notes.Count > 0;

    partial void OnSelectedSubjectChanged(Subject? value) => _ = LoadNotesAsync();

    /// <summary>Склеивает отмеченные заметки в markdown: заголовок второго уровня + текст, по порядку списка.</summary>
    public string BuildMarkdown() => string.Join(
        "\n",
        Notes.Where(n => n.IsSelected).Select(n => $"## {n.Title}\n\n{n.ContentMarkdown}\n"));

    // Номер последней запрошенной загрузки: ответ устаревшего запроса (быстро сменили предмет)
    // отбрасывается — запросы идут на пуле и могут наложиться (Phase 13.10).
    private int _loadVersion;

    private async Task LoadNotesAsync()
    {
        var version = ++_loadVersion;
        Notes.Clear();
        CanSave = false;
        OnPropertyChanged(nameof(HasNotes));

        if (SelectedSubject is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var list = await _notes.GetBySubjectAsync(SelectedSubject.Id);
            if (version != _loadVersion)
            {
                return;
            }

            foreach (var note in list)
            {
                Notes.Add(new NoteSelectionRowViewModel(note, UpdateCanSave));
            }

            OnPropertyChanged(nameof(HasNotes));
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
            }
        }
    }

    private void UpdateCanSave() => CanSave = Notes.Any(n => n.IsSelected);
}
