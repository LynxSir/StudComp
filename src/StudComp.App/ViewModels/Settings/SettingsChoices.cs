using StudComp.Infrastructure.Settings;

namespace StudComp.ViewModels.Settings;

/// <summary>Пункт выбора темы: значение + подпись для UI.</summary>
public sealed record ThemeChoice(AppTheme Value, string Display);

/// <summary>Пункт выбора формата даты.</summary>
public sealed record DateFormatChoice(DateFormatStyle Value, string Display);

/// <summary>Пункт выбора плотности интерфейса.</summary>
public sealed record DensityChoice(AppDensity Value, string Display);

/// <summary>Ступень масштаба интерфейса: множитель + подпись (напр. «110 %»).</summary>
public sealed record FontScaleChoice(double Value, string Display);

/// <summary>Пресет акцентного цвета: ключ (пишется в настройки) + hex + подпись.</summary>
public sealed record AccentChoice(string Key, string Hex, string Display);

/// <summary>Готовые наборы значений для выпадающих списков раздела «Оформление».</summary>
public static class SettingsChoices
{
    /// <summary>Варианты темы.</summary>
    public static IReadOnlyList<ThemeChoice> Themes { get; } =
    [
        new(AppTheme.Light, "Светлая"),
        new(AppTheme.Dark, "Тёмная"),
        new(AppTheme.System, "Как в системе"),
    ];

    /// <summary>Варианты формата даты.</summary>
    public static IReadOnlyList<DateFormatChoice> DateFormats { get; } =
    [
        new(DateFormatStyle.System, "Как в системе"),
        new(DateFormatStyle.DayMonthYearDots, "ДД.ММ.ГГГГ"),
        new(DateFormatStyle.Iso, "ГГГГ-ММ-ДД"),
    ];

    /// <summary>Варианты плотности.</summary>
    public static IReadOnlyList<DensityChoice> Densities { get; } =
    [
        new(AppDensity.Normal, "Обычная"),
        new(AppDensity.Compact, "Компактная"),
    ];

    /// <summary>Ступени масштаба интерфейса.</summary>
    public static IReadOnlyList<FontScaleChoice> FontScales { get; } =
    [
        new(0.9, "90 %"),
        new(1.0, "100 %"),
        new(1.1, "110 %"),
        new(1.25, "125 %"),
        new(1.4, "140 %"),
    ];

    /// <summary>Пресеты акцентного цвета. Первый — винный по умолчанию (ARCHITECTURE §19.2).</summary>
    public static IReadOnlyList<AccentChoice> AccentPresets { get; } =
    [
        new("wine", "#8A1C2B", "Винный"),
        new("gold", "#B8863B", "Золотой"),
        new("ocean", "#1F5F8B", "Океан"),
        new("forest", "#2E6B4F", "Хвоя"),
        new("plum", "#6D3B79", "Слива"),
        new("graphite", "#4A4A52", "Графит"),
    ];
}
