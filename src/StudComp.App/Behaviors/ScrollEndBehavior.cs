using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StudComp.Behaviors;

/// <summary>
/// Догрузка следующей страницы, когда прокрутка подошла к концу списка (new_addons.md §4.6).
/// </summary>
/// <remarks>
/// Поиск всегда отдаёт страницу в полсотни строк, но требовать сотню нажатий «Показать ещё», чтобы
/// добраться до конца пяти тысяч карточек, — издевательство. Кнопка при этом остаётся: она нужна
/// тому, кто ходит по разделу с клавиатуры, и служит запасным путём, если событие не пришло.
/// </remarks>
public static class ScrollEndBehavior
{
    /// <summary>За сколько экранов до конца просить следующую страницу.</summary>
    private const double TriggerViewports = 1.5;

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(ScrollEndBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    /// <summary>Команда догрузки. Вызывается только когда она сама считает себя выполнимой.</summary>
    public static ICommand? GetCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        // Событие всплывает от внутреннего ScrollViewer, поэтому слушаем его на самом списке —
        // лезть в шаблон за именованным элементом не нужно.
        element.RemoveHandler(ScrollViewer.ScrollChangedEvent, (ScrollChangedEventHandler)OnScrollChanged);

        if (e.NewValue is ICommand)
        {
            element.AddHandler(ScrollViewer.ScrollChangedEvent, (ScrollChangedEventHandler)OnScrollChanged);
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not DependencyObject element || GetCommand(element) is not { } command)
        {
            return;
        }

        // Прокрутка вверх и изменение размеров списка догрузку не просят.
        if (e.VerticalChange <= 0 || e.ViewportHeight <= 0)
        {
            return;
        }

        var remaining = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight);
        if (remaining > e.ViewportHeight * TriggerViewports)
        {
            return;
        }

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
