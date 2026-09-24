using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StudComp.Core.Domain;

namespace StudComp.Controls;

/// <summary>
/// Виртуализирующая панель-сетка: плитки одинакового размера, перенос по строкам, создаются только
/// видимые (new_addons.md §8.2).
/// </summary>
/// <remarks>
/// <para>
/// В стандартном WPF виртуализирующей wrap-панели нет: <see cref="WrapPanel"/> честно создаёт все
/// элементы, и на пяти тысячах карточек список умирает. Пакеты в проекте заморожены, зато
/// прецеденты собственных контролов есть (<c>GradeTrendChart</c>, <c>FadeContentControl</c>).
/// </para>
/// <para>
/// Вся арифметика — в <see cref="VirtualWrapLayout"/> (<c>Core</c>, покрыт табличными тестами);
/// здесь остаётся только работа с контейнерами WPF. Расчёт держится на равном размере плиток —
/// именно поэтому <see cref="ItemWidth"/> и <see cref="ItemHeight"/> задаются явно, а не меряются
/// по содержимому: измерять тысячи элементов ради раскладки означало бы отменить виртуализацию.
/// </para>
/// </remarks>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    /// <summary>Сколько пикселей проматывает одна «строка» — колесо и стрелки полосы прокрутки.</summary>
    private const double ScrollLineDelta = 48;

    /// <summary>Сколько строк колесо мыши проматывает за щелчок.</summary>
    private const int WheelLines = 3;

    private Size _extent = new(0, 0);
    private Size _viewport = new(0, 0);
    private double _verticalOffset;
    private int _columns = 1;

    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(240d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(150d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Ширина плитки вместе с её полями.</summary>
    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>Высота плитки вместе с её полями.</summary>
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var count = owner?.Items.Count ?? 0;

        // Ширина у панели всегда есть (её задаёт ScrollViewer), а высота при вертикальной прокрутке
        // приходит бесконечной — окном считаем то, что реально видно.
        var viewportWidth = double.IsInfinity(availableSize.Width) ? _viewport.Width : availableSize.Width;
        var viewportHeight = double.IsInfinity(availableSize.Height) ? _viewport.Height : availableSize.Height;

        var metrics = VirtualWrapLayout.Compute(
            count,
            ItemWidth,
            ItemHeight,
            viewportWidth,
            viewportHeight,
            _verticalOffset);

        _columns = metrics.Columns;

        RealizeRange(metrics.FirstVisibleIndex, metrics.LastVisibleIndex);
        CleanUpRange(metrics.FirstVisibleIndex, metrics.LastVisibleIndex);

        UpdateScrollInfo(new Size(viewportWidth, metrics.ExtentHeight), new Size(viewportWidth, viewportHeight));

        return new Size(
            double.IsInfinity(availableSize.Width) ? _columns * ItemWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? metrics.ExtentHeight : availableSize.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0)
            {
                continue;
            }

            var (x, y) = VirtualWrapLayout.PositionOf(itemIndex, _columns, ItemWidth, ItemHeight);
            child.Arrange(new Rect(x, y - _verticalOffset, ItemWidth, ItemHeight));
        }

        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;

            case NotifyCollectionChangedAction.Reset:
                // Новый запрос — новый список: остаться на прежнем смещении значило бы показать
                // пользователю пустоту вместо первой страницы результатов.
                RemoveInternalChildRange(0, InternalChildren.Count);
                SetVerticalOffset(0);
                break;
        }

        base.OnItemsChanged(sender, args);
    }

    /// <summary>Создать контейнеры видимого диапазона; уже созданные переиспользуются.</summary>
    private void RealizeRange(int first, int last)
    {
        if (last < first)
        {
            return;
        }

        var generator = ItemContainerGenerator;
        var startPosition = generator.GeneratorPositionFromIndex(first);

        // Если позиция попала внутрь существующего контейнера, вставлять надо следующим за ним.
        var childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (var itemIndex = first; itemIndex <= last; itemIndex++, childIndex++)
            {
                if (generator.GenerateNext(out var isNewlyRealized) is not UIElement child)
                {
                    break;
                }

                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(ItemWidth, ItemHeight));
            }
        }
    }

    /// <summary>
    /// Отдать генератору контейнеры, уехавшие за пределы видимости.
    /// </summary>
    /// <remarks>
    /// Контейнеры именно освобождаются (<c>Remove</c>), а не перерабатываются (<c>Recycle</c>):
    /// переработка требует возвращать пришедший из генератора контейнер обратно в детей панели, и
    /// половинчатая её поддержка даёт пустые места на экране. Плитка дешёвая, освобождения хватает,
    /// а <c>VirtualizingPanel.VirtualizationMode</c> на списке с этой панелью поэтому не ставится.
    /// </remarks>
    private void CleanUpRange(int first, int last)
    {
        var generator = ItemContainerGenerator;

        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);

            if (itemIndex >= first && itemIndex <= last)
            {
                continue;
            }

            generator.Remove(position, 1);
            RemoveInternalChildRange(i, 1);
        }
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        var changed = extent != _extent || viewport != _viewport;
        _extent = extent;
        _viewport = viewport;

        var maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        if (_verticalOffset > maxOffset)
        {
            _verticalOffset = maxOffset;
            changed = true;
        }

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    // ---- IScrollInfo -----------------------------------------------------------------------
    // Панель сама себе полоса прокрутки: только так ScrollViewer узнаёт настоящий размер списка,
    // не создавая ни одного невидимого элемента.

    /// <inheritdoc />
    public bool CanVerticallyScroll { get; set; } = true;

    /// <summary>Сетка переносит плитки по ширине окна, поэтому горизонтальной прокрутки нет.</summary>
    public bool CanHorizontallyScroll { get; set; }

    /// <inheritdoc />
    public double ExtentWidth => _extent.Width;

    /// <inheritdoc />
    public double ExtentHeight => _extent.Height;

    /// <inheritdoc />
    public double ViewportWidth => _viewport.Width;

    /// <inheritdoc />
    public double ViewportHeight => _viewport.Height;

    /// <inheritdoc />
    public double HorizontalOffset => 0;

    /// <inheritdoc />
    public double VerticalOffset => _verticalOffset;

    /// <inheritdoc />
    public ScrollViewer? ScrollOwner { get; set; }

    /// <inheritdoc />
    public void LineUp() => SetVerticalOffset(_verticalOffset - ScrollLineDelta);

    /// <inheritdoc />
    public void LineDown() => SetVerticalOffset(_verticalOffset + ScrollLineDelta);

    /// <inheritdoc />
    public void PageUp() => SetVerticalOffset(_verticalOffset - _viewport.Height);

    /// <inheritdoc />
    public void PageDown() => SetVerticalOffset(_verticalOffset + _viewport.Height);

    /// <inheritdoc />
    public void MouseWheelUp() => SetVerticalOffset(_verticalOffset - (WheelLines * ScrollLineDelta));

    /// <inheritdoc />
    public void MouseWheelDown() => SetVerticalOffset(_verticalOffset + (WheelLines * ScrollLineDelta));

    /// <inheritdoc />
    public void SetVerticalOffset(double offset)
    {
        var maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        var target = double.IsNaN(offset) ? 0 : Math.Clamp(offset, 0, maxOffset);

        if (Math.Abs(target - _verticalOffset) < 0.5)
        {
            return;
        }

        _verticalOffset = target;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    /// <summary>
    /// Показать элемент целиком — этим ходит клавиатура и <c>ScrollIntoView</c>. Пока контейнера
    /// нет, прямоугольник не вычислить, поэтому смещение считается по индексу.
    /// </summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as UIElement;
        if (child is null)
        {
            return rectangle;
        }

        var childIndex = InternalChildren.IndexOf(child);
        if (childIndex < 0)
        {
            return rectangle;
        }

        var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
        if (itemIndex < 0)
        {
            return rectangle;
        }

        SetVerticalOffset(VirtualWrapLayout.OffsetToReveal(
            itemIndex,
            _columns,
            ItemHeight,
            _viewport.Height,
            _verticalOffset));

        var (x, y) = VirtualWrapLayout.PositionOf(itemIndex, _columns, ItemWidth, ItemHeight);
        return new Rect(x, y - _verticalOffset, ItemWidth, ItemHeight);
    }

    /// <summary>Прокрутить к элементу по его индексу — используется навигацией с клавиатуры.</summary>
    public void ScrollToIndex(int itemIndex)
    {
        if (itemIndex < 0)
        {
            return;
        }

        SetVerticalOffset(VirtualWrapLayout.OffsetToReveal(
            itemIndex,
            _columns,
            ItemHeight,
            _viewport.Height,
            _verticalOffset));
    }

    /// <summary>Горизонтальной прокрутки у сетки нет — методы обязаны существовать, но ничего не делают.</summary>
    public void LineLeft()
    {
    }

    /// <inheritdoc cref="LineLeft" />
    public void LineRight()
    {
    }

    /// <inheritdoc cref="LineLeft" />
    public void PageLeft()
    {
    }

    /// <inheritdoc cref="LineLeft" />
    public void PageRight()
    {
    }

    /// <inheritdoc cref="LineLeft" />
    public void MouseWheelLeft()
    {
    }

    /// <inheritdoc cref="LineLeft" />
    public void MouseWheelRight()
    {
    }

    /// <inheritdoc cref="LineLeft" />
    public void SetHorizontalOffset(double offset)
    {
    }
}
