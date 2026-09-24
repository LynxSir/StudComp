using System.Windows;
using System.Windows.Media.Animation;

namespace StudComp.Services;

/// <summary>
/// Токены движения для код-behind (Phase 13.10): длительности переходов и общая easing-функция.
/// Раньше длительности лежали в <c>Application.Resources</c> (<c>MotionDuration*</c>) и
/// переписывались при переключении тумблера «Анимации». Запись в словарь ресурсов приложения на
/// живом окне — обход всего визуального дерева с инвалидацией ссылок, и именно на нём слой
/// композиции WPF падал (<c>ArgumentException</c> из <c>DUCE.ReleaseOnChannel</c>), после чего
/// ломалась любая выгрузка страницы. Ни один XAML эти ключи не читал — их читал только код, поэтому
/// теперь код читает их отсюда, а словарь при переключении не трогается вовсе.
/// </summary>
/// <remarks>
/// Значения меняет только <see cref="MotionService"/> на UI-потоке; читаются они тоже на UI-потоке
/// (в момент запуска анимации), поэтому синхронизация не нужна. При выключенных анимациях все три
/// длительности нулевые — сторибоарды отрабатывают мгновенно, один рубильник на всё приложение.
/// </remarks>
public static class Motion
{
    /// <summary>Быстрый переход (сдвиг рельса, палитра) — 130 мс при включённых анимациях.</summary>
    public static Duration Fast { get; internal set; } = new(TimeSpan.FromMilliseconds(MotionService.FastMs));

    /// <summary>Обычный переход (смена раздела, модалка) — 220 мс.</summary>
    public static Duration Medium { get; internal set; } = new(TimeSpan.FromMilliseconds(MotionService.MediumMs));

    /// <summary>Медленный переход — 340 мс.</summary>
    public static Duration Slow { get; internal set; } = new(TimeSpan.FromMilliseconds(MotionService.SlowMs));

    /// <summary>Общая easing-функция «замедление к концу»; заморожена — безопасно делить между анимациями.</summary>
    public static IEasingFunction EaseOut { get; } = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Easing «плавно с обеих сторон».</summary>
    public static IEasingFunction EaseInOut { get; } = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    private static CubicEase Frozen(CubicEase ease)
    {
        ease.Freeze();
        return ease;
    }
}
