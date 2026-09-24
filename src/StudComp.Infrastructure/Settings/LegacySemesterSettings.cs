namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Старая секция <c>Rubrica:Organizer</c> из <c>usersettings.json</c>, где до Phase 12.3 жили дата
/// начала семестра и чётность первой недели. Читается ровно один раз при запуске — чтобы перенести
/// значения в сущность <c>Semester</c>, после чего секция стирается
/// (<see cref="SettingsMigrations"/>). Новый код на неё не опирается.
/// </summary>
public sealed class LegacySemesterSettings
{
    /// <summary>Имя устаревшей секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Organizer";

    public DateOnly? SemesterStartDate { get; set; }

    public bool FirstWeekIsOdd { get; set; } = true;
}
