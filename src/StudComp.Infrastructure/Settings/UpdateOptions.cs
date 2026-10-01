namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Проверка обновлений через Velopack. Секция конфигурации <c>Rubrica:Update</c>
/// (ARCHITECTURE §13, §11.6 — единственный сетевой вызов, с opt-out).
/// </summary>
public sealed class UpdateOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Update";

    /// <summary>Проверять ли обновления автоматически при запуске (opt-out, §11.6).</summary>
    public bool AutoCheckOnStartup { get; set; } = true;

    /// <summary>URL репозитория с релизами Velopack (публичная информация, допустима в appsettings).</summary>
    public string GithubRepoUrl { get; set; } = "https://github.com/LynxSir/StudComp";

    /// <summary>Учитывать ли предрелизные версии при проверке.</summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>Версия, которую пользователь решил пропустить. Новая версия снова будет показана.</summary>
    public string SkippedVersion { get; set; } = string.Empty;
}
