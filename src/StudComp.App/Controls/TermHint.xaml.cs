using System.Windows;
using System.Windows.Controls;

namespace StudComp.Controls;

/// <summary>
/// Маленькая кнопка-«?» с всплывающим пояснением термина рядом с меткой поля — лёгкий встроенный
/// онбординг без отдельного мастера первого запуска (Phase 13.9, new_addons.md §15).
/// </summary>
public partial class TermHint : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TermHint), new PropertyMetadata(string.Empty));

    public TermHint()
    {
        InitializeComponent();

        // StaysOpen=False закрывает Popup по клику мимо без отклика на IsChecked тумблера — без
        // ручной синхронизации первый клик после автозакрытия впустую переключал бы IsChecked
        // обратно на true→false, и открыть подсказку снова понадобился бы второй клик.
        HintPopup.Closed += (_, _) => Toggle.IsChecked = false;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
