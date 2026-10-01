using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StudComp.Core.Domain;

namespace StudComp.Behaviors;

/// <summary>
/// Автосинтаксис Markdown в поле ввода (new_addons.md §11.2, §11.3, §11.6): продолжение списков и
/// цитат по <c>Enter</c>, вложенность по <c>Tab</c>, обёртка выделения по <c>Ctrl+B</c>/<c>Ctrl+I</c>,
/// перенос строки по клику ниже последней строки, парные скобки/кавычки/<c>$</c> при наборе.
/// </summary>
/// <remarks>
/// Что именно сделать, решает <see cref="MarkdownEditing"/> в <c>Core</c> — там это покрыто
/// табличными тестами, а здесь остаётся только применить правку. Цепляется к базовому
/// <see cref="TextBox"/>, поэтому работает и с <c>ui:TextBox</c> из WPF-UI, и с обычным.
/// Правка применяется через <c>Select</c> + <c>SelectedText</c> внутри <c>BeginChange</c>:
/// присваивание <c>Text</c> целиком снесло бы стек отмены, а без <c>BeginChange</c> замена
/// выделения легла бы в историю двумя шагами вместо одного.
/// </remarks>
public static class MarkdownEditingBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(MarkdownEditingBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        textBox.PreviewKeyDown -= OnPreviewKeyDown;
        textBox.PreviewTextInput -= OnPreviewTextInput;
        textBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        textBox.SelectionChanged -= OnSelectionChanged;
        textBox.PreviewDragOver -= OnPreviewDragOver;

        if (e.NewValue is true)
        {
            // Туннелирование доходит раньше TextBoxBase.OnKeyDown, где живут AcceptsReturn/AcceptsTab,
            // поэтому перехватить ввод можно только здесь. Сами символы приходят отдельным событием
            // PreviewTextInput — по нему и ставятся парные скобки. SelectionChanged держит каретку
            // снаружи вставленных картинок (new_addons.md §12), PreviewDragOver не даёт уронить
            // перетаскиваемый текст внутрь ссылки.
            textBox.PreviewKeyDown += OnPreviewKeyDown;
            textBox.PreviewTextInput += OnPreviewTextInput;
            textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            textBox.SelectionChanged += OnSelectionChanged;
            textBox.PreviewDragOver += OnPreviewDragOver;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsReadOnly)
        {
            return;
        }

        // Во время композиции IME клавиши принадлежат редактору ввода, а не нам.
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        // Картинка — цельный объект: стрелки перескакивают её, Backspace/Delete удаляют целиком в
        // два нажатия. Обязано сработать раньше DeletePair, поэтому стоит до общего switch.
        if (TryHandleImageToken(textBox, e))
        {
            e.Handled = true;
            return;
        }

        var text = textBox.Text;
        var modifiers = Keyboard.Modifiers;

        var edit = e.Key switch
        {
            // При выделении Enter должен заменить его переводом строки — это штатное поведение,
            // и подставлять туда маркер списка было бы неожиданно.
            Key.Enter when modifiers == ModifierKeys.None
                && textBox.AcceptsReturn
                && textBox.SelectionLength == 0 =>
                MarkdownEditing.SplitDisplayMath(text, textBox.CaretIndex)
                    ?? MarkdownEditing.ContinueLine(text, textBox.CaretIndex),

            Key.Back when modifiers == ModifierKeys.None && textBox.SelectionLength == 0 =>
                MarkdownEditing.DeletePair(text, textBox.CaretIndex),

            Key.Tab when modifiers is ModifierKeys.None or ModifierKeys.Shift =>
                MarkdownEditing.Indent(
                    text,
                    textBox.SelectionStart,
                    textBox.SelectionLength,
                    outdent: modifiers == ModifierKeys.Shift),

            Key.B when modifiers == ModifierKeys.Control =>
                MarkdownEditing.ToggleWrap(text, textBox.SelectionStart, textBox.SelectionLength, "**"),

            Key.I when modifiers == ModifierKeys.Control =>
                MarkdownEditing.ToggleWrap(text, textBox.SelectionStart, textBox.SelectionLength, "*"),

            _ => null,
        };

        if (edit is { } value && Apply(textBox, value))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Набран символ: парная скобка, обёртка выделения или перепрыгивание закрывающей
    /// (new_addons.md §11.3). Ввод через IME и вставка нескольких символов сразу — не наш случай.
    /// </summary>
    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsReadOnly || e.Text is not { Length: 1 })
        {
            return;
        }

        if (Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift))
        {
            return;
        }

        var edit = MarkdownEditing.AutoPair(textBox.Text, textBox.SelectionStart, textBox.SelectionLength, e.Text);
        if (edit is { } value && Apply(textBox, value))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Клик по пустому месту ниже текста добавляет строку и встаёт на неё. Сам по себе
    /// <see cref="TextBox"/> в этом случае лишь переставляет каретку в ближайшую позицию
    /// последней строки — новой строки не появляется (new_addons.md §11.6).
    /// </summary>
    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsReadOnly || !textBox.AcceptsReturn)
        {
            return;
        }

        var text = textBox.Text;
        if (text.Length == 0)
        {
            return;
        }

        var lastCharacter = textBox.GetRectFromCharacterIndex(text.Length);
        if (lastCharacter.IsEmpty)
        {
            return;
        }

        var point = e.GetPosition(textBox);

        // Два условия вместе: клик ниже последней строки и под курсором действительно нет символа
        // (справа от короткой строки второе условие тоже верно, но первое — уже нет).
        if (point.Y <= lastCharacter.Bottom || textBox.GetCharacterIndexFromPoint(point, false) >= 0)
        {
            return;
        }

        if (MarkdownEditing.AppendTrailingLine(text) is not { } edit)
        {
            // Пустая строка в конце уже есть — просто встаём в неё.
            textBox.Focus();
            textBox.CaretIndex = text.Length;
            e.Handled = true;
            return;
        }

        textBox.Focus();
        if (Apply(textBox, edit))
        {
            // Иначе TextBox тут же переставит каретку в конец последней строки сам.
            e.Handled = true;
        }
    }

    /// <summary>
    /// Применяет правку одним шагом отмены. Возвращает <c>false</c>, если текст успел уйти вперёд.
    /// Публичный — им же пользуется редактор заметки, когда правку инициирует вьюмодель
    /// (вставка картинки, изменение размера, перенос): так она попадает в стек <c>Ctrl+Z</c>.
    /// </summary>
    public static bool Apply(TextBox textBox, MarkdownEdit edit)
    {
        if (edit.Start < 0 || edit.Start + edit.Length > textBox.Text.Length)
        {
            return false;
        }

        var snapping = BeginSnapping(textBox);
        try
        {
            textBox.BeginChange();
            try
            {
                textBox.Select(edit.Start, edit.Length);
                textBox.SelectedText = edit.Replacement;
            }
            finally
            {
                textBox.EndChange();
            }

            // Каретка выставляется ПОСЛЕ закрытия блока изменения: внутри него позиция принадлежит
            // ещё не закрытому шагу отмены, и WPF вправе вернуть её к началу правки — ровно жалоба
            // «курсор ставится перед знаками, а не внутри» (new_addons.md §11.3).
            var caret = Math.Clamp(edit.Start + edit.CaretOffset, 0, textBox.Text.Length);
            var length = Math.Clamp(edit.SelectionLength, 0, textBox.Text.Length - caret);

            if (length > 0)
            {
                textBox.Select(caret, length);
            }
            else
            {
                textBox.CaretIndex = caret;
            }

            SetLastCaret(textBox, textBox.CaretIndex);
            ReassertCaret(textBox, caret, length);
        }
        finally
        {
            EndSnapping(textBox, snapping);
        }

        return true;
    }

    /// <summary>
    /// Картинка как цельный объект (new_addons.md §12): стрелка у её границы перескакивает ссылку
    /// целиком, а <c>Backspace</c>/<c>Delete</c> удаляют картинку в два нажатия — первое выделяет
    /// её как подтверждение, второе удаляет. Что именно делать, решает
    /// <see cref="MarkdownImageEditing"/>; здесь только применение.
    /// </summary>
    private static bool TryHandleImageToken(TextBox textBox, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var text = textBox.Text;

        if (e.Key is Key.Left or Key.Right
            && modifiers is ModifierKeys.None or ModifierKeys.Shift
            && textBox.SelectionLength == 0
            && MarkdownImageEditing.StepOverToken(text, textBox.CaretIndex, e.Key == Key.Right) is { } target)
        {
            var anchor = textBox.CaretIndex;
            if (modifiers == ModifierKeys.Shift)
            {
                SelectQuietly(textBox, Math.Min(anchor, target), Math.Abs(target - anchor));
            }
            else
            {
                SelectQuietly(textBox, target, 0);
            }

            return true;
        }

        if (e.Key is not (Key.Back or Key.Delete) || modifiers != ModifierKeys.None || textBox.IsReadOnly)
        {
            return false;
        }

        if (MarkdownImageEditing.Eraser(text, textBox.SelectionStart, textBox.SelectionLength, e.Key == Key.Delete)
            is not { } erase)
        {
            return false;
        }

        if (erase.Step == ImageEraseStep.Select)
        {
            SelectQuietly(textBox, erase.Token.Start, erase.Token.Length);
            return true;
        }

        return Apply(textBox, erase.Edit);
    }

    /// <summary>
    /// Каретка не должна оказываться внутри ссылки на картинку, а выделение — накрывать её
    /// половину. Одна проверка здесь закрывает клик, двойной клик, протяжку, <c>Ctrl+стрелка</c> и
    /// визуальные <c>Home</c>/<c>End</c> при переносе строк — отдельные обработчики мыши не нужны.
    /// </summary>
    private static void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || (bool)textBox.GetValue(SnappingProperty))
        {
            return;
        }

        var snapped = MarkdownImageEditing.Snap(
            textBox.Text, textBox.SelectionStart, textBox.SelectionLength, GetLastCaret(textBox));

        if (snapped is { } target)
        {
            SelectQuietly(textBox, target.Start, target.Length);
            return;
        }

        SetLastCaret(textBox, textBox.CaretIndex);
    }

    /// <summary>
    /// Уронить перетаскиваемый текст внутрь ссылки на картинку нельзя — она бы разорвалась и
    /// перестала быть картинкой. Сам перенос картинки при этом работает штатным механизмом
    /// <see cref="TextBox"/>: нажатие внутри токена уже выделило его целиком.
    /// </summary>
    private static void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsReadOnly)
        {
            return;
        }

        var index = textBox.GetCharacterIndexFromPoint(e.GetPosition(textBox), true);
        if (index >= 0 && MarkdownImageEditing.Inside(textBox.Text, index) is not null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Вернуть каретку на место, если после прохода ввода её кто-то сдвинул. Сдвинуть могут вещи
    /// вне поведения: обновление привязки, пересборка разметки поля, перечитывание списка заметок
    /// после автосохранения. Жалоба «курсор ставится перед знаками, а не внутри» воспроизвести на
    /// стенде не удалось (поведение ставит каретку верно и после Tab, и после Alt), поэтому здесь
    /// стоит не догадка о причине, а проверка результата.
    /// </summary>
    /// <remarks>
    /// Восстановление происходит только если текст с тех пор не менялся: иначе быстрый набор
    /// воевал бы сам с собой. Длина текста и есть признак «пользователь успел напечатать ещё».
    /// </remarks>
    private static void ReassertCaret(TextBox textBox, int caret, int length)
    {
        var expectedTextLength = textBox.Text.Length;

        _ = textBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (textBox.Text.Length != expectedTextLength
                    || (textBox.SelectionStart == caret && textBox.SelectionLength == length))
                {
                    return;
                }

                SelectQuietly(textBox, caret, length);
            });
    }

    /// <summary>Выставить каретку или выделение, не вызывая повторный разбор в <see cref="OnSelectionChanged"/>.</summary>
    private static void SelectQuietly(TextBox textBox, int start, int length)
    {
        var previous = BeginSnapping(textBox);
        try
        {
            if (length > 0)
            {
                textBox.Select(start, length);
            }
            else
            {
                textBox.CaretIndex = start;
            }
        }
        finally
        {
            EndSnapping(textBox, previous);
        }

        SetLastCaret(textBox, textBox.CaretIndex);
    }

    /// <summary>Где была каретка до события — этим определяется направление её движения.</summary>
    private static readonly DependencyProperty LastCaretProperty = DependencyProperty.RegisterAttached(
        "LastCaret", typeof(int), typeof(MarkdownEditingBehavior), new PropertyMetadata(-1));

    /// <summary>Идёт наша собственная правка выделения — перехватывать её повторно нельзя.</summary>
    private static readonly DependencyProperty SnappingProperty = DependencyProperty.RegisterAttached(
        "Snapping", typeof(bool), typeof(MarkdownEditingBehavior), new PropertyMetadata(false));

    private static int GetLastCaret(DependencyObject element) => (int)element.GetValue(LastCaretProperty);

    private static void SetLastCaret(DependencyObject element, int value) =>
        element.SetValue(LastCaretProperty, value);

    private static bool BeginSnapping(DependencyObject element)
    {
        var previous = (bool)element.GetValue(SnappingProperty);
        element.SetValue(SnappingProperty, true);
        return previous;
    }

    private static void EndSnapping(DependencyObject element, bool previous) =>
        element.SetValue(SnappingProperty, previous);
}
