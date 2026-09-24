using System.Windows;
using System.Windows.Controls;

namespace StudComp.Controls;

/// <summary>
/// Хост страниц навигации (Phase 13.10). Кешируемые разделы сайдбара (<see cref="Services.IPersistentPage"/>)
/// живут в дереве <b>постоянно</b> и переключаются через <see cref="UIElement.Visibility"/>;
/// прочие страницы показываются через обычный <see cref="ContentPresenter"/> по <c>DataTemplate</c>.
/// </summary>
/// <remarks>
/// Почему не просто <c>ContentControl.Content = view</c>: при смене содержимого-<c>UIElement</c>
/// <see cref="ContentPresenter"/> идёт путём <c>DetachGeneratedSubTree → FreeContent</c>, и на нём
/// в этом приложении воспроизводимо падал слой композиции WPF (<c>ArgumentException</c> из
/// <c>DUCE.ReleaseOnChannel</c>) — проверено прогоном. Скрытая через <c>Collapsed</c> страница из
/// дерева не отсоединяется, её ресурсы на канале не освобождаются, и этого пути просто нет. Заодно
/// повторный показ дешевле: нет обхода наследуемых свойств по всему поддереву.
/// </remarks>
public sealed class PageHost : Grid
{
    /// <summary>Что показать: готовый View кешируемого раздела либо ViewModel для <c>DataTemplate</c>.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(object),
        typeof(PageHost),
        new PropertyMetadata(null, (d, e) => ((PageHost)d).OnSourceChanged(e.NewValue)));

    private readonly ContentPresenter _transient = new() { Visibility = Visibility.Collapsed };

    public PageHost()
    {
        Children.Add(_transient);
    }

    /// <inheritdoc cref="SourceProperty"/>
    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private void OnSourceChanged(object? source)
    {
        if (source is FrameworkElement view)
        {
            // Некешируемую страницу отпускаем — её дерево строилось шаблоном и уходит вместе с VM.
            _transient.Content = null;
            _transient.Visibility = Visibility.Collapsed;

            if (!Children.Contains(view))
            {
                Children.Add(view);
            }

            foreach (UIElement child in Children)
            {
                if (!ReferenceEquals(child, _transient))
                {
                    child.Visibility = ReferenceEquals(child, view) ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            return;
        }

        foreach (UIElement child in Children)
        {
            if (!ReferenceEquals(child, _transient))
            {
                child.Visibility = Visibility.Collapsed;
            }
        }

        _transient.Content = source;
        _transient.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
