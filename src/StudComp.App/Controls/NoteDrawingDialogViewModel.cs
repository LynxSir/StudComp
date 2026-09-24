using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;

namespace StudComp.Controls;

/// <summary>
/// Тонкая VM диалога рисования (new_addons.md §12, Phase 13.7): сама не читает и не пишет диск — все
/// файловые операции делает вызывающая сторона (<c>NoteEditorViewModel</c>) после подтверждения.
/// <see cref="Document"/>/<see cref="PngBytes"/> код-behind диалога обновляет немедленно на каждое
/// событие <see cref="NoteDrawingCanvas.Changed"/>, поэтому к моменту, когда <c>ShowEditorAsync</c>
/// возвращает подтверждение, данные уже готовы — ждать закрытия диалога не нужно.
/// </summary>
public sealed partial class NoteDrawingDialogViewModel : ObservableObject
{
    /// <summary>Существующий рисунок для повторного редактирования; <see langword="null"/> — новый.</summary>
    public NoteDrawingDocument? InitialDocument { get; }

    public NoteDrawingDialogViewModel(NoteDrawingDocument? initialDocument = null)
    {
        InitialDocument = initialDocument;
    }

    [ObservableProperty]
    private bool _canSave;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    public NoteDrawingDocument? Document { get; set; }

    public byte[]? PngBytes { get; set; }
}
