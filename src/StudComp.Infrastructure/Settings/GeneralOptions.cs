namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Формат даты в интерфейсе (new_addons.md §7 §1). Пока задел — общий форматтер дат вводится
/// отдельной задачей; значение сохраняется, но ещё не применяется повсеместно.
/// </summary>
public enum DateFormatStyle
{
    /// <summary>Как в системе (короткий формат Windows).</summary>
    System = 0,

    /// <summary>ДД.ММ.ГГГГ.</summary>
    DayMonthYearDots = 1,

    /// <summary>ГГГГ-ММ-ДД (ISO 8601).</summary>
    Iso = 2,
}

/// <summary>
/// Общие настройки приложения. Секция конфигурации <c>Rubrica:General</c> (ARCHITECTURE §11.2, §11.5).
/// </summary>
public sealed class GeneralOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:General";

    /// <summary>
    /// Запускать Rubrica при входе в Windows. Реализуется без прав администратора через ключ реестра
    /// <c>HKCU\...\Run</c> (ARCHITECTURE §11.6).
    /// </summary>
    public bool RunOnWindowsStartup { get; set; }

    /// <summary>
    /// Стартовать со свёрнутым в трей окном — не показывать главное окно при запуске
    /// (new_addons.md §7 §1). Полезно в паре с автозапуском.
    /// </summary>
    public bool LaunchMinimized { get; set; }

    /// <summary>
    /// Сворачивать в трей вместо завершения при закрытии главного окна — чтобы Архивариус продолжал
    /// работать в фоне (ARCHITECTURE §11.5). По умолчанию включено.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>
    /// Язык интерфейса. Пока только <c>ru</c> — задел под локализацию (new_addons.md §7 §1).
    /// </summary>
    public string Language { get; set; } = "ru";

    /// <summary>Формат даты в интерфейсе. Задел (см. <see cref="DateFormatStyle"/>).</summary>
    public DateFormatStyle DateFormat { get; set; } = DateFormatStyle.System;

    /// <summary>
    /// Запомненные положение и состояние окна (new_addons.md §1.2). <see langword="null"/> — запуск
    /// развёрнутым (в т. ч. при первом запуске).
    /// </summary>
    public WindowPlacementInfo? WindowPlacement { get; set; }
}
