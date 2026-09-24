using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StudComp.Services;

namespace StudComp.Controls;

/// <summary>
/// Карточка с переворотом (new_addons.md §8.3): при раскрытии ответа контент сменяется через сжатие
/// по горизонтали — тот самый «переворот», только без честной трёхмерной сцены, которой здесь и не надо.
/// </summary>
/// <remarks>
/// Длительность берётся из <see cref="Motion.Medium"/>. При выключенных анимациях она нулевая, и
/// смена мгновенная — ровно приём <see cref="FadeContentControl"/>, никаких ветвлений в коде сессии.
/// </remarks>
public sealed class FlipCard : ContentControl
{
    private const double HalfTurn = 0.5;

    public static readonly DependencyProperty IsFlippedProperty = DependencyProperty.Register(
        nameof(IsFlipped),
        typeof(bool),
        typeof(FlipCard),
        new PropertyMetadata(false, OnIsFlippedChanged));

    static FlipCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlipCard), new FrameworkPropertyMetadata(typeof(ContentControl)));
    }

    public FlipCard()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new ScaleTransform(1, 1);
    }

    /// <summary>Карточка перевёрнута — ответ раскрыт.</summary>
    public bool IsFlipped
    {
        get => (bool)GetValue(IsFlippedProperty);
        set => SetValue(IsFlippedProperty, value);
    }

    private static void OnIsFlippedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlipCard card && card.IsLoaded)
        {
            card.Flip();
        }
    }

    private void Flip()
    {
        var transform = RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        RenderTransform = transform;

        var duration = Motion.Medium;

        if (duration.TimeSpan <= TimeSpan.Zero)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.ScaleX = 1;
            return;
        }

        var half = new Duration(TimeSpan.FromTicks((long)(duration.TimeSpan.Ticks * HalfTurn)));
        var ease = Motion.EaseInOut;

        var animation = new DoubleAnimationUsingKeyFrames { Duration = duration };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.02, KeyTime.FromTimeSpan(half.TimeSpan), ease));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(duration.TimeSpan), ease));

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
    }
}
