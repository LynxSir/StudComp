using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using StudComp.Core.Domain;

namespace StudComp.Behaviors;

/// <summary>
/// Подсветка совпадений в строке выдачи: приложенное свойство наполняет <see cref="TextBlock"/>
/// последовательностью <see cref="Run"/>, где совпавшие куски выделены (new_addons.md §4.5).
/// </summary>
/// <remarks>
/// Разметки в тексте карточки нет и не будет: поиск отдаёт куски, а не HTML, и превращать их в
/// <c>Inlines</c> дешевле, чем городить свой рендерер. Свойство переживает переиспользование
/// контейнеров виртуализацией — при каждой смене значения <c>Inlines</c> собираются заново.
/// </remarks>
public static class InlineHighlightBehavior
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments",
        typeof(IReadOnlyList<CardTextSegment>),
        typeof(InlineHighlightBehavior),
        new PropertyMetadata(null, OnSegmentsChanged));

    /// <summary>Куски текста, которые надо показать. Пусто — блок очищается.</summary>
    public static IReadOnlyList<CardTextSegment>? GetSegments(DependencyObject element) =>
        (IReadOnlyList<CardTextSegment>?)element.GetValue(SegmentsProperty);

    public static void SetSegments(DependencyObject element, IReadOnlyList<CardTextSegment>? value) =>
        element.SetValue(SegmentsProperty, value);

    private static void OnSegmentsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBlock block)
        {
            return;
        }

        block.Inlines.Clear();

        if (e.NewValue is not IReadOnlyList<CardTextSegment> segments || segments.Count == 0)
        {
            return;
        }

        var highlight = block.TryFindResource("Cards.HighlightBrush") as System.Windows.Media.Brush;

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.IsMatch)
            {
                run.FontWeight = FontWeights.SemiBold;
                if (highlight is not null)
                {
                    run.Background = highlight;
                }
            }

            block.Inlines.Add(run);
        }
    }
}
