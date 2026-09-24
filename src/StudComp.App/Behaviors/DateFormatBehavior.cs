using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace StudComp.Behaviors;

/// <summary>
/// Единый формат отображаемой даты в стоковом <see cref="DatePicker"/> (new_addons.md §8,
/// Тест.txt №24): <c>SelectedDateFormat</c> умеет только Long/Short (культурные), произвольную
/// строку штатное API не принимает. Поведение переписывает текст внутренней части шаблона
/// <c>DatePickerTextBox</c> после того, как WPF уже синхронизировала её сама — <see
/// cref="DatePicker.Loaded"/>/<see cref="DatePicker.SelectedDateChanged"/> — публичные события,
/// собственная внутренняя синхронизация контрола гарантированно отрабатывает раньше подписчиков.
/// Если часть шаблона не найдена (нестандартный шаблон) — тихий no-op, статус-кво не хуже прежнего.
/// </summary>
public static class DateFormatBehavior
{
    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.RegisterAttached(
            "Format", typeof(string), typeof(DateFormatBehavior), new PropertyMetadata(null, OnFormatChanged));

    public static string? GetFormat(DependencyObject d) => (string?)d.GetValue(FormatProperty);

    public static void SetFormat(DependencyObject d, string? value) => d.SetValue(FormatProperty, value);

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DatePicker picker)
        {
            return;
        }

        picker.Loaded -= OnLoaded;
        picker.SelectedDateChanged -= OnSelectedDateChanged;

        if (e.NewValue is string)
        {
            picker.Loaded += OnLoaded;
            picker.SelectedDateChanged += OnSelectedDateChanged;
            ApplyFormat(picker);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => ApplyFormat((DatePicker)sender);

    private static void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker picker)
        {
            ApplyFormat(picker);
        }
    }

    private static void ApplyFormat(DatePicker picker)
    {
        if (GetFormat(picker) is not { } format || picker.SelectedDate is not { } date)
        {
            return;
        }

        if (picker.Template?.FindName("DatePickerTextBox", picker) is DatePickerTextBox textBox)
        {
            var text = date.ToString(format, CultureInfo.InvariantCulture);
            if (textBox.Text != text)
            {
                textBox.Text = text;
            }
        }
    }
}
