namespace StudComp.Infrastructure.Settings;

/// <summary>Тема оформления (ARCHITECTURE §11.5).</summary>
public enum AppTheme
{
    /// <summary>Следовать за системной темой Windows.</summary>
    System = 0,

    /// <summary>Всегда светлая.</summary>
    Light = 1,

    /// <summary>Всегда тёмная.</summary>
    Dark = 2,
}

/// <summary>
/// Плотность интерфейса (new_addons.md §7 §2). Компактный режим уменьшает отступы и высоту строк —
/// удобно на маленьких экранах и при большом количестве данных.
/// </summary>
public enum AppDensity
{
    /// <summary>Обычные отступы (по умолчанию).</summary>
    Normal = 0,

    /// <summary>Уплотнённые отступы и меньшая высота строк.</summary>
    Compact = 1,
}

/// <summary>
/// Настройки внешнего вида. Секция конфигурации <c>Rubrica:Appearance</c> (ARCHITECTURE §11.2).
/// </summary>
public sealed class AppearanceOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Appearance";

    /// <summary>Винный акцент по умолчанию (ARCHITECTURE §19.2).</summary>
    public const string DefaultAccent = "wine";

    private double _fontScale = 1.0;

    /// <summary>
    /// Выбранная тема. По умолчанию — светлая (основная тема продукта, new_addons.md §1.2);
    /// тёмная и системная — по выбору пользователя.
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.Light;

    /// <summary>
    /// Акцентный цвет: ключ пресета (<c>wine</c>/<c>gold</c>/<c>ocean</c>/<c>forest</c>/<c>plum</c>/
    /// <c>graphite</c>) либо строка <c>#RRGGBB</c> для произвольного цвета. Пустое/непонятное значение
    /// трактуется как винный. Разбирается сервисом движения UI.
    /// </summary>
    public string Accent { get; set; } = DefaultAccent;

    /// <summary>Плотность интерфейса. По умолчанию — обычная.</summary>
    public AppDensity Density { get; set; } = AppDensity.Normal;

    /// <summary>
    /// Масштаб интерфейса (как «зум» в редакторе): множитель <c>LayoutTransform</c> корня окна.
    /// Зажимается в диапазон <c>[0.8, 1.4]</c>. По умолчанию 1.0.
    /// </summary>
    public double FontScale
    {
        get => _fontScale;
        set => _fontScale = double.IsFinite(value) ? Math.Clamp(value, 0.8, 1.4) : 1.0;
    }

    /// <summary>
    /// Разрешены ли анимации переходов и микровзаимодействий (ARCHITECTURE §19.4). Выключение делает
    /// все переходы мгновенными. По умолчанию включено.
    /// </summary>
    public bool AnimationsEnabled { get; set; } = true;
}
