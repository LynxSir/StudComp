namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Настройки уведомлений. Секция <c>Rubrica:Notifications</c> (ARCHITECTURE §9.5, §11.2).
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Notifications";

    /// <summary>Показывать напоминания о дедлайнах и парах.</summary>
    public bool RemindersEnabled { get; set; } = true;

    /// <summary>За сколько минут до события напоминать.</summary>
    public int RemindMinutesBefore { get; set; } = 30;

    /// <summary>
    /// Не показывать напоминания в «тихие часы» (new_addons.md §7 §6). Планировщик пропускает показ,
    /// если текущее время попадает в окно <see cref="QuietHoursStart"/>..<see cref="QuietHoursEnd"/>.
    /// </summary>
    public bool QuietHoursEnabled { get; set; }

    /// <summary>Начало тихих часов.</summary>
    public TimeOnly QuietHoursStart { get; set; } = new(22, 0);

    /// <summary>
    /// Конец тихих часов. Если меньше начала — окно считается переходящим через полночь.
    /// </summary>
    public TimeOnly QuietHoursEnd { get; set; } = new(8, 0);

    /// <summary>Проигрывать звук при показе напоминания.</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>
    /// Напоминать, если файл не разобран N дней (0 — выключено). Задел: сканер «залежавшихся» файлов
    /// вводится отдельной задачей, значение пока только сохраняется.
    /// </summary>
    public int UnsortedReminderDays { get; set; }
}
