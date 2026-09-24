namespace StudComp.Core.Common;

/// <summary>
/// Единая точка правды по путям к пользовательским данным продукта. Всё лежит под именем продукта —
/// <c>%LocalAppData%\Rubrica\</c> (ARCHITECTURE §7, §11.1, ADR §16.13).
/// </summary>
/// <remarks>
/// Живёт в <c>Core</c>, потому что и <c>Data</c>, и <c>Infrastructure</c> ссылаются на <c>Core</c>, но
/// не друг на друга (ARCHITECTURE §5.1) — это единственное общее место. Класс не тянет зависимостей
/// сверх BCL (<see cref="Environment"/> + <see cref="Path"/>), так что запрет «Core — только BCL»
/// не нарушается (проверяется тестом <c>CoreDependenciesTests</c>).
/// </remarks>
public static class RubricaPaths
{
    /// <summary>Корневой каталог данных продукта: <c>%LocalAppData%\Rubrica</c>.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rubrica");

    /// <summary>Файл базы данных по умолчанию: <c>%LocalAppData%\Rubrica\rubrica.db</c>.</summary>
    public static string DatabaseFile { get; } = Path.Combine(DataDirectory, "rubrica.db");

    /// <summary>Каталог лог-файлов: <c>%LocalAppData%\Rubrica\logs</c> (ARCHITECTURE §11.1).</summary>
    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");

    /// <summary>
    /// Каталог сгенерированных отчётов по умолчанию: <c>%LocalAppData%\Rubrica\Reports</c>
    /// (ARCHITECTURE §10.1). Пользователь может выбрать другой в настройках.
    /// </summary>
    public static string ReportsDirectory { get; } = Path.Combine(DataDirectory, "Reports");

    /// <summary>
    /// Файл пользовательских настроек — оверлей поверх заводского <c>appsettings.json</c>
    /// (ARCHITECTURE §11.2): <c>%LocalAppData%\Rubrica\usersettings.json</c>.
    /// </summary>
    public static string UserSettingsFile { get; } = Path.Combine(DataDirectory, "usersettings.json");

    /// <summary>
    /// Каталог по умолчанию для резервных копий (new_addons.md §7 §9):
    /// <c>%LocalAppData%\Rubrica\Backups</c>. Пользователь может выбрать другой путь при сохранении.
    /// </summary>
    public static string BackupsDirectory { get; } = Path.Combine(DataDirectory, "Backups");

    /// <summary>
    /// Строка подключения SQLite к базе по указанному пути либо к <see cref="DatabaseFile"/>,
    /// если путь не задан.
    /// </summary>
    public static string BuildSqliteConnectionString(string? databasePath = null) =>
        $"Data Source={databasePath ?? DatabaseFile}";
}
