using System.Windows;
using System.Windows.Controls;

namespace StudComp.Views.Archivist.Dialogs;

/// <summary>
/// Форма правила сортировки для модального диалога. Вся логика — во ViewModel; единственное
/// оправданное исключение — вставка токена по позиции курсора в <c>RenameTemplateBox</c>
/// (<see cref="TextBox.SelectionStart"/> недоступен из VM без утечки WPF-типов, Phase 12.2).
/// </summary>
public partial class RuleEditor : UserControl
{
    public RuleEditor() => InitializeComponent();

    private void OnInsertToken(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string token })
        {
            return;
        }

        var caret = RenameTemplateBox.SelectionStart;
        var text = RenameTemplateBox.Text;
        RenameTemplateBox.Text = text.Insert(caret, token);
        RenameTemplateBox.SelectionStart = caret + token.Length;
        RenameTemplateBox.Focus();
    }
}
