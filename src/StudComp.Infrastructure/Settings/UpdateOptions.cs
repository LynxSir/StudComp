namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Проверка обновлений через Velopack. Секция конфигурации <c>Rubrica:Update</c>
/// (ARCHITECTURE §13, §11.6 — единственный сетевой вызов, с opt-out).
/// </summary>
/// <remarks>
/// <see cref="GithubToken"/> в <c>appsettings.json</c> всегда пуст: приватный репозиторий требует
/// токен, и хранится он только в пользовательском <c>usersettings.json</c>
/// (<c>%LocalAppData%\Rubrica\</c>), который не входит ни в репозиторий, ни в дистрибутив. Пустой
/// токен → автопроверка молча выключена.
/// </remarks>
public sealed class UpdateOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Update";

    /// <summary>Проверять ли обновления автоматически при запуске (opt-out, §11.6).</summary>
    public bool AutoCheckOnStartup { get; set; } = true;

    /// <summary>URL репозитория с релизами Velopack (публичная информация, допустима в appsettings).</summary>
    public string GithubRepoUrl { get; set; } = "https://github.com/LynxSir/StudComp";

    /// <summary>Токен доступа для приватного репозитория. Задаётся только в usersettings.json.</summary>
    public string GithubToken { get; set; } = string.Empty;

    /// <summary>Учитывать ли предрелизные версии при проверке.</summary>
    public bool IncludePrerelease { get; set; }
}
