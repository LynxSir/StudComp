using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StudComp.Services;

namespace StudComp.Controls;

/// <summary>
/// <see cref="ContentControl"/>, который при смене источника коротко проявляет новый контент (fade +
/// подъём на 8 px). Используется для правой панели модалки настроек при переключении раздела.
/// Длительность берётся из <see cref="Motion.Medium"/> — при выключенных анимациях она нулевая и
/// смена мгновенная. Старый контент не «замораживается» снимком (это источник утечек/мигания) — он
/// просто исчезает, а новый появляется.
/// </summary>
/// <remarks>
/// Источник задаётся через <see cref="Source"/>, а не через <see cref="ContentControl.Content"/>:
/// View для каждого объекта-источника строится по <c>DataTemplate</c> <b>один раз</b> и дальше
/// переиспользуется (Phase 13.10). Разделы настроек — singleton-VM с десятками карточек и
/// контролов WPF-UI, и пересобирать их дерево при каждом клике по рельсу — это те самые 130+ мс
/// «залипания», которые видны в пробе отзывчивости.
/// </remarks>
public sealed class FadeContentControl : ContentControl
{
    /// <summary>Объект (обычно ViewModel), для которого показывается View.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(object),
        typeof(FadeContentControl),
        new PropertyMetadata(null, (d, e) => ((FadeContentControl)d).OnSourceChanged(e.NewValue)));

    private readonly Dictionary<object, FrameworkElement> _views = new(ReferenceEqualityComparer.Instance);

    static FadeContentControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FadeContentControl),
            new FrameworkPropertyMetadata(typeof(ContentControl)));
    }

    /// <inheritdoc cref="SourceProperty"/>
    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private void OnSourceChanged(object? source)
    {
        if (source is null)
        {
            Content = null;
            return;
        }

        if (!_views.TryGetValue(source, out var view))
        {
            view = BuildView(source);
            _views[source] = view;
        }

        Content = view;
    }

    /// <summary>
    /// Строит View по неявному <c>DataTemplate</c> для типа источника — ровно так же, как это сделал
    /// бы <see cref="ContentPresenter"/>, только результат сохраняется. Если шаблона нет, показывается
    /// сам объект (текстом), как и у обычного <see cref="ContentControl"/>.
    /// </summary>
    private FrameworkElement BuildView(object source)
    {
        var key = new DataTemplateKey(source.GetType());
        if (TryFindResource(key) is DataTemplate template && template.LoadContent() is FrameworkElement view)
        {
            view.DataContext = source;
            return view;
        }

        return new ContentPresenter { Content = source };
    }

    /// <inheritdoc />
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        var duration = Motion.Medium;
        var transform = EnsureTranslateTransform();

        if (!IsLoaded || newContent is null || duration.TimeSpan <= TimeSpan.Zero)
        {
            BeginAnimation(OpacityProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            Opacity = 1;
            transform.Y = 0;
            return;
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = Motion.EaseOut,
        });

        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From = 8,
            To = 0,
            Duration = duration,
            EasingFunction = Motion.EaseOut,
        });
    }

    private TranslateTransform EnsureTranslateTransform()
    {
        if (RenderTransform is TranslateTransform existing)
        {
            return existing;
        }

        var transform = new TranslateTransform();
        RenderTransform = transform;
        return transform;
    }
}
