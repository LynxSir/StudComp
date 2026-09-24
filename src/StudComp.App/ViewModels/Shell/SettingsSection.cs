namespace StudComp.ViewModels.Shell;

/// <summary>
/// Разделы модалки настроек — для дип-линков <c>OpenSettings(section)</c> из других экранов
/// (new_addons.md §1.6). Полный набор из 11 разделов (new_addons.md §7, §11). Значения нигде не
/// персистятся — только транзитный <see cref="OpenSettingsMessage"/>, поэтому переименования и
/// порядок безопасны.
/// </summary>
public enum SettingsSection
{
    /// <summary>Общие: язык, свёрнутый запуск, трей при закрытии, формат даты.</summary>
    General,

    /// <summary>Оформление: тема, акцент, плотность, масштаб, анимации.</summary>
    Appearance,

    /// <summary>Учебная папка: путь Study Root, шаблон подпапок предмета.</summary>
    Workspace,

    /// <summary>Архивариус: наблюдение, папки, игнор-паттерны, пороги, продвинутое.</summary>
    Archivist,

    /// <summary>Расписание и семестр: семестры, сетка звонков, напоминание до пары.</summary>
    Semester,

    /// <summary>Дедлайны и уведомления: напоминания, тихие часы, звук.</summary>
    Notifications,

    /// <summary>Отчёты и титульный лист: папка вывода, автооткрытие, данные студента.</summary>
    Reports,

    /// <summary>Автозапуск: запуск с Windows, свёрнутый запуск.</summary>
    Autostart,

    /// <summary>Данные и резервные копии: пути, бэкап/восстановление, ретеншн, временные файлы.</summary>
    Data,

    /// <summary>Картотека: повторение, тренировка, напоминания, поисковый индекс.</summary>
    Cards,

    /// <summary>О программе: версия, лицензия, ссылки, диагностика, лог.</summary>
    About,
}
