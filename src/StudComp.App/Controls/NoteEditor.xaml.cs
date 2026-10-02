using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using StudComp.Behaviors;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;

namespace StudComp.Controls;

/// <summary>
/// Редактор заметки. Код-behind — принудительное сохранение на потерю фокуса (событие
/// <c>LostFocus</c> из VM не видно), «Сделать карточку» на выделении (new_addons.md §7.2):
/// <c>TextBox.SelectedText</c> — UI-специфичное состояние, VM его не видит, — и переход из
/// просмотра в правку по клику (new_addons.md §11.5), где нужен кликнутый <c>Run</c>.
/// </summary>
public partial class NoteEditor : UserControl, INoteTextEditor
{
    private Point _previewMouseDown;
    private bool _importingImage;

    private sealed record ImageTransfer(string[] Paths, BitmapSource? Bitmap);

    public NoteEditor()
    {
        InitializeComponent();
        BindImagePaste(ContentTextBox);
        BindImagePaste(PreviewViewer);

        // DataContext подставляется хозяином уже после конструктора (HubNotes.xaml), поэтому
        // подписываемся на его смену, а не на текущее значение.
        DataContextChanged += OnDataContextChanged;
    }

    private NoteEditorViewModel? ViewModel => DataContext as NoteEditorViewModel;

    private void BindImagePaste(UIElement target)
    {
        var binding = new CommandBinding(ApplicationCommands.Paste);
        binding.PreviewCanExecute += OnImagePasteCanExecute;
        binding.CanExecute += OnImagePasteCanExecute;
        binding.PreviewExecuted += OnImagePasteExecuted;
        target.CommandBindings.Add(binding);
    }

    private void OnImagePasteCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (ViewModel is not { HasNote: true } || _importingImage) return;
        try
        {
            if (Clipboard.GetDataObject() is { } data && HasImage(data))
            {
                e.CanExecute = true;
                e.Handled = true;
            }
        }
        catch (Exception ex) when (IsTransferError(ex)) { }
    }

    private async void OnImagePasteExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (ViewModel is not { HasNote: true } vm || _importingImage) return;
        try
        {
            if (Clipboard.GetDataObject() is not { } data || !HasImage(data)) return;
            e.Handled = true;
            var transfer = ReadImage(data);
            await ImportImageAsync(vm, transfer, vm.IsPreview ? vm.Content.Length : ContentTextBox.SelectionStart,
                vm.IsPreview ? 0 : ContentTextBox.SelectionLength);
        }
        catch (Exception ex) when (IsTransferError(ex))
        {
            e.Handled = true;
            vm.ReportImagePasteError(ex);
        }
    }

    private static bool HasImage(IDataObject data) =>
        data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent("PNG")
        || (data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(NoteEditorViewModel.IsSupportedImage));

    private static ImageTransfer ReadImage(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(NoteEditorViewModel.IsSupportedImage))
            return new ImageTransfer(paths.Where(NoteEditorViewModel.IsSupportedImage).ToArray(), null);

        // PNG от браузеров и графических редакторов сохраняет прозрачность точнее формата DIB.
        if (data.GetData("PNG") is Stream stream)
        {
            var originalPosition = stream.CanSeek ? stream.Position : 0;
            try
            {
                if (stream.CanSeek) stream.Position = 0;
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var bitmap = decoder.Frames[0];
                bitmap.Freeze();
                return new ImageTransfer([], bitmap);
            }
            finally
            {
                if (stream.CanSeek) stream.Position = originalPosition;
            }
        }
        if (data.GetData(DataFormats.Bitmap) is BitmapSource image) return new ImageTransfer([], image);
        throw new NotSupportedException("Буфер содержит изображение в неподдерживаемом формате.");
    }

    private static bool IsTransferError(Exception error) => error is ExternalException or IOException
        or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException;

    private async Task ImportImageAsync(NoteEditorViewModel vm, ImageTransfer transfer, int caret, int selectionLength = 0)
    {
        if (_importingImage) return;
        _importingImage = true;
        var readOnly = ContentTextBox.IsReadOnly;
        ContentTextBox.IsReadOnly = true;
        try
        {
            var after = transfer.Bitmap is { } bitmap
                ? await vm.InsertClipboardImageAsync(bitmap, caret, selectionLength)
                : await vm.InsertImagesAsync(transfer.Paths, caret, selectionLength);
            if (ReferenceEquals(ViewModel, vm) && !vm.IsPreview && after is { } position) OnEditRequested(this, position);
        }
        finally
        {
            ContentTextBox.IsReadOnly = readOnly;
            _importingImage = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnImageDragOver(object sender, DragEventArgs e)
    {
        try
        {
            if (!HasImage(e.Data)) return;
            e.Effects = ViewModel is { HasNote: true } && !_importingImage
                && e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        catch (Exception ex) when (IsTransferError(ex))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private async void OnImageDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { HasNote: true } vm) return;
        try
        {
            if (!HasImage(e.Data)) return;
            e.Handled = true;
            if (_importingImage || !e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return;
            var caret = vm.IsPreview ? vm.Content.Length : ContentTextBox.GetCharacterIndexFromPoint(e.GetPosition(ContentTextBox), true);
            await ImportImageAsync(vm, ReadImage(e.Data), caret < 0 ? vm.Content.Length : caret);
            e.Effects = DragDropEffects.Copy;
        }
        catch (Exception ex) when (IsTransferError(ex))
        {
            e.Handled = true;
            vm.ReportImagePasteError(ex);
        }
    }

    private async void OnInsertImageClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var after = await vm.PickImageAsync(vm.IsPreview ? vm.Content.Length : ContentTextBox.CaretIndex);
        if (!vm.IsPreview && after is { } position) OnEditRequested(this, position);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is NoteEditorViewModel previous)
        {
            previous.EditRequested -= OnEditRequested;
            previous.AttachEditor(null);
        }

        if (e.NewValue is NoteEditorViewModel current)
        {
            current.EditRequested += OnEditRequested;
            current.AttachEditor(this);
        }
    }

    /// <summary>
    /// Поставить каретку и забрать фокус можно только после того, как поле ввода станет видимым:
    /// в момент переключения режима оно ещё <c>Collapsed</c>, не имеет разметки и фокус не примет.
    /// </summary>
    private void OnEditRequested(object? sender, int caretIndex) =>
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                ContentTextBox.Focus();
                ContentTextBox.CaretIndex = Math.Clamp(caretIndex, 0, ContentTextBox.Text.Length);
                ContentTextBox.ScrollToLine(
                    ContentTextBox.GetLineIndexFromCharacterIndex(ContentTextBox.CaretIndex));
            });

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.FlushAsync();
        }
    }

    /// <summary>
    /// «Рисование» (new_addons.md §12): каретка нужна из code-behind (UI-состояние, VM её не видит) —
    /// в правке берём фактическую позицию курсора, в просмотре вставляем в конец текста.
    /// </summary>
    private async void OnInsertDrawingClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var wasEditing = !viewModel.IsPreview;
        var caret = wasEditing ? ContentTextBox.CaretIndex : viewModel.Content.Length;

        var caretAfter = await viewModel.InsertDrawingAsync(caret);
        if (wasEditing && caretAfter is { } newCaret)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () => ContentTextBox.CaretIndex = Math.Clamp(newCaret, 0, ContentTextBox.Text.Length));
        }
    }

    /// <summary>Esc возвращает из правки в просмотр — второй способ переключения, кроме кнопки.</summary>
    private void OnContentPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.IsPreview = true;
        e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        _previewMouseDown = e.GetPosition(PreviewViewer);

    /// <summary>
    /// Клик по содержимому предпросмотра открывает правку примерно в этом же месте; клик по вставленному
    /// рисунку (new_addons.md §12) открывает его повторно на редактирование вместо перехода в правку
    /// текста. Протяжку выделения и двойной клик пропускаем: там пользователь хочет скопировать текст.
    /// </summary>
    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        // Картинка разбирается ДО проверок протяжки и выделения: клик по BlockUIContainer во
        // FlowDocument сам выделяет вложенный объект, поэтому с обратным порядком меню не
        // открывалось вовсе (жалоба «не редактируется размер изображения»).
        if (FindImage(e.OriginalSource as DependencyObject) is { Tag: ImageBlock image } element)
        {
            e.Handled = true;
            ShowImageMenu(viewModel, element, image);
            return;
        }

        var up = e.GetPosition(PreviewViewer);
        if (e.ClickCount > 1
            || Math.Abs(up.X - _previewMouseDown.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(up.Y - _previewMouseDown.Y) > SystemParameters.MinimumVerticalDragDistance
            || PreviewViewer.Selection is { IsEmpty: false })
        {
            return;
        }

        // Мышиные события во FlowDocument приходят от самих TextElement, поэтому кликнутый кусок
        // текста достаётся из OriginalSource — у FlowDocumentScrollViewer нет GetPositionFromPoint.
        var clicked = FindRun(e.OriginalSource as DependencyObject);
        if (clicked is null)
        {
            // Клик мимо текста (поля, пустое место) — режим не меняем.
            return;
        }

        viewModel.BeginEditAt(viewModel.LocateInSource(clicked.Text));
        e.Handled = true;
    }

    private static Run? FindRun(DependencyObject? source)
    {
        for (var node = source; node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is Hyperlink)
            {
                // Ссылку обрабатывает она сама.
                return null;
            }

            if (node is Run run)
            {
                return run;
            }
        }

        return null;
    }

    private static Image? FindImage(DependencyObject? source)
    {
        for (var node = source; node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is Image image)
            {
                return image;
            }
        }

        return null;
    }

    private void OnContentContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        MakeCardMenuItem.IsEnabled = ContentTextBox.SelectionLength > 0;
    }

    private void OnMakeCardFromSelectionClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.RequestCardFromSelection(ContentTextBox.SelectedText);
        }
    }

    // --- INoteTextEditor: вьюмодель правит текст через живое поле ввода ---------------------

    /// <inheritdoc />
    public bool IsLive => ContentTextBox.IsVisible;

    /// <inheritdoc />
    public int Caret => ContentTextBox.CaretIndex;

    /// <inheritdoc />
    public bool TryApply(MarkdownEdit edit)
    {
        // На время копирования ввод закрыт, но сама вставка должна пройти через стек Undo.
        var unlock = _importingImage && ContentTextBox.IsReadOnly;
        if (unlock) ContentTextBox.IsReadOnly = false;
        try { return MarkdownEditingBehavior.Apply(ContentTextBox, edit); }
        finally { if (unlock) ContentTextBox.IsReadOnly = true; }
    }

    /// <inheritdoc />
    public void ResetUndoHistory()
    {
        // Документированный способ выбросить буфер отмены у TextBox.
        ContentTextBox.IsUndoEnabled = false;
        ContentTextBox.IsUndoEnabled = true;
    }

    /// <inheritdoc />
    public bool TryUndo() => ContentTextBox.CanUndo && ContentTextBox.Undo();

    private void OnUndoCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = ViewModel is { HasNote: true, IsPreview: true } && ContentTextBox.CanUndo;
        e.Handled = e.CanExecute;
    }

    private void OnUndoExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        ViewModel?.UndoFromPreview();
        e.Handled = true;
    }

    /// <summary>
    /// Меню картинки в предпросмотре: размер, правка рисунка, перемещение и удаление. Свободного
    /// перетаскивания тут нет намеренно — <see cref="FlowDocumentScrollViewer"/> не отдаёт позицию
    /// сброса, и цель была бы угадыванием; «выше»/«ниже» детерминированы.
    /// </summary>
    private static void ShowImageMenu(NoteEditorViewModel viewModel, Image element, ImageBlock image)
    {
        var menu = new ContextMenu { PlacementTarget = element };

        AddItem(menu, "Изменить размер…", () => viewModel.ResizeImageAsync(image));
        AddItem(menu, "Редактировать рисунок…", () => viewModel.EditDrawingAtAsync(image.PathOrBase64));
        menu.Items.Add(new Separator());
        AddItem(menu, "Переместить выше", () => viewModel.MoveImageAsync(image, up: true));
        AddItem(menu, "Переместить ниже", () => viewModel.MoveImageAsync(image, up: false));
        menu.Items.Add(new Separator());
        AddItem(menu, "Удалить изображение", () => viewModel.DeleteImageAsync(image));

        menu.IsOpen = true;

        static void AddItem(ContextMenu menu, string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }
    }
}
