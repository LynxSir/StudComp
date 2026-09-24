using Microsoft.Extensions.Logging;

namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Однократные миграции пользовательских настроек между версиями продукта. Вызывается из
/// <c>App.OnStartup</c> после сборки хоста, до показа окна. Все шаги идемпотентны — повторный запуск
/// на уже мигрированном файле ничего не меняет.
/// </summary>
public static class SettingsMigrations
{
    /// <summary>Прогоняет все миграции настроек над оверлеем <c>usersettings.json</c>.</summary>
    public static void Run(UserSettingsProvider settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        UnifyStudyRootWithArchiveRoot(settings, logger);
    }

    /// <summary>
    /// Читает устаревшую секцию <c>Rubrica:Organizer</c> — источник разового сида сущности
    /// <c>Semester</c> (new_addons.md §5). Сама сущность живёт в БД, поэтому шаг не может быть
    /// обычной миграцией настроек: значение читает App и передаёт в Органайзер.
    /// </summary>
    public static LegacySemesterSettings ReadLegacySemester(UserSettingsProvider settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Get<LegacySemesterSettings>(LegacySemesterSettings.SectionName);
    }

    /// <summary>
    /// Гасит перенесённые значения устаревшей секции, чтобы они не выглядели действующей настройкой.
    /// Идемпотентен: повторный вызов на уже пустой секции ничего не делает.
    /// </summary>
    public static void ClearLegacySemester(UserSettingsProvider settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        var legacy = settings.Get<LegacySemesterSettings>(LegacySemesterSettings.SectionName);
        if (legacy.SemesterStartDate is null)
        {
            return;
        }

        settings.Update<LegacySemesterSettings>(
            LegacySemesterSettings.SectionName, o => o.SemesterStartDate = null);
        logger.LogInformation("Миграция настроек: дата начала семестра перенесена в сущность «Семестр»");
    }

    /// <summary>
    /// Study Root унифицирован с архивариусом (new_addons.md §1.1): если учебная папка ещё не выбрана,
    /// а корень архива задан — берём его как учебную папку.
    /// </summary>
    private static void UnifyStudyRootWithArchiveRoot(UserSettingsProvider settings, ILogger logger)
    {
        var workspace = settings.Get<WorkspaceOptions>(WorkspaceOptions.SectionName);
        if (!string.IsNullOrWhiteSpace(workspace.StudyRootPath))
        {
            return;
        }

        var archiveRoot = settings.Get<ArchivistOptions>(ArchivistOptions.SectionName).ArchiveRootFolder;
        if (string.IsNullOrWhiteSpace(archiveRoot))
        {
            return;
        }

        settings.Update<WorkspaceOptions>(WorkspaceOptions.SectionName, o => o.StudyRootPath = archiveRoot);
        logger.LogInformation(
            "Миграция настроек: учебная папка взята из корня архива архивариуса: {Path}", archiveRoot);
    }
}
