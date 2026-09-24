using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        if (e.NewValue is true)
        {
            // Туннелирование доходит раньше TextBoxBase.OnKeyDown, где живут AcceptsReturn/AcceptsTab,
            // поэтому перехватить ввод можно только здесь. Сами символы приходят отдельным событием
            // PreviewTextInput — по нему и ставятся парные скобки.
            textBox.PreviewKeyDown += OnPreviewKeyDown;
            textBox.PreviewTextInput += OnPreviewTextInput;
            textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
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

    /// <summary>Применяет правку одним шагом отмены. Возвращает <c>false</c>, если текст успел уйти вперёд.</summary>
    private static bool Apply(TextBox textBox, MarkdownEdit edit)
    {
        if (edit.Start < 0 || edit.Start + edit.Length > textBox.Text.Length)
        {
            return false;
        }

        textBox.BeginChange();
        try
        {
            textBox.Select(edit.Start, edit.Length);
            textBox.SelectedText = edit.Replacement;

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
        }
        finally
        {
            textBox.EndChange();
        }

        return true;
    }
}
