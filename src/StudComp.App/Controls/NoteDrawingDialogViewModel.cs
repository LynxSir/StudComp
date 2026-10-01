using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.Services;

namespace StudComp.Controls;

/// <summary>
/// Форма рисования: состояние кнопок плюс результат — модель рисунка и её снимок в PNG.
/// </summary>
/// <remarks>
/// <see cref="Document"/> и <see cref="PngBytes"/> — обычные свойства без оповещений: их читают
/// один раз после закрытия диалога. Заполняются они в момент подтверждения
/// (<see cref="OnConfirming"/>), а не на каждое изменение холста: полный рендер и PNG-кодирование
/// в UI-потоке на каждый штрих и на каждый задетый ластиком штрих и были той самой
/// «задумчивостью» рисования.
/// </remarks>
public sealed partial class NoteDrawingDialogViewModel : ObservableObject, IDialogConfirmHook
{
    public NoteDrawingDialogViewModel(NoteDrawingDocument? initialDocument = null) =>
        InitialDocument = initialDocument;

    /// <summary>Рисунок, открытый на повторное редактирование, либо <see langword="null"/>.</summary>
    public NoteDrawingDocument? InitialDocument { get; }

    [ObservableProperty]
    private bool _canSave;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    /// <summary>Модель рисунка — заполняется при подтверждении.</summary>
    public NoteDrawingDocument? Document { get; private set; }

    /// <summary>Снимок холста в PNG — заполняется при подтверждении.</summary>
    public byte[]? PngBytes { get; private set; }

    /// <summary>
    /// Чем снять результат с холста. Ставит код-behind формы: сама вьюмодель про холст не знает.
    /// </summary>
    public Func<(NoteDrawingDocument Document, byte[] Png)>? Capture { get; set; }

    public void OnConfirming()
    {
        if (Capture is not { } capture)
        {
            return;
        }

        var (document, png) = capture();
        Document = document;
        PngBytes = png;
    }
}
