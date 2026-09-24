using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Controls;

/// <summary>
/// Редактор заметки. Код-behind — принудительное сохранение на потерю фокуса (событие
/// <c>LostFocus</c> из VM не видно), «Сделать карточку» на выделении (new_addons.md §7.2):
/// <c>TextBox.SelectedText</c> — UI-специфичное состояние, VM его не видит, — и переход из
/// просмотра в правку по клику (new_addons.md §11.5), где нужен кликнутый <c>Run</c>.
/// </summary>
public partial class NoteEditor : UserControl
{
    private Point _previewMouseDown;

    public NoteEditor()
    {
        InitializeComponent();

        // DataContext подставляется хозяином уже после конструктора (HubNotes.xaml), поэтому
        // подписываемся на его смену, а не на текущее значение.
        DataContextChanged += OnDataContextChanged;
    }

    private NoteEditorViewModel? ViewModel => DataContext as NoteEditorViewModel;

    private void OnImageDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = ViewModel is { HasNote: true }
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && paths.Any(NoteEditorViewModel.IsSupportedImage) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnImageDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { HasNote: true } vm || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        e.Handled = true;
        var caret = vm.IsPreview ? vm.Content.Length : ContentTextBox.GetCharacterIndexFromPoint(e.GetPosition(ContentTextBox), true);
        var after = await vm.InsertImagesAsync(paths, caret < 0 ? vm.Content.Length : caret);
        if (!vm.IsPreview && after is { } position) OnEditRequested(this, position);
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
        }

        if (e.NewValue is NoteEditorViewModel current)
        {
            current.EditRequested += OnEditRequested;
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

        var up = e.GetPosition(PreviewViewer);
        if (e.ClickCount > 1
            || Math.Abs(up.X - _previewMouseDown.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(up.Y - _previewMouseDown.Y) > SystemParameters.MinimumVerticalDragDistance
            || PreviewViewer.Selection is { IsEmpty: false })
        {
            return;
        }

        if (FindImage(e.OriginalSource as DependencyObject) is { Tag: ImageBlock image } element)
        {
            e.Handled = true;
            var menu = new ContextMenu { PlacementTarget = element };
            var resize = new MenuItem { Header = "Изменить размер…" };
            resize.Click += async (_, _) => await viewModel.ResizeImageAsync(image);
            var edit = new MenuItem { Header = "Редактировать рисунок…" };
            edit.Click += async (_, _) => await viewModel.EditDrawingAtAsync(image.PathOrBase64);
            menu.Items.Add(resize);
            menu.Items.Add(edit);
            menu.IsOpen = true;
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
}
