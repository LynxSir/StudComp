namespace StudComp.Data;

/// <summary>
/// Приводит базу в рабочее состояние при старте приложения: создаёт каталог данных и накатывает
/// все невыполненные миграции (ARCHITECTURE §6, §7). Вызывается из <c>App.xaml.cs</c> после
/// <c>_host.StartAsync()</c> (Phase 3).
/// </summary>
public interface IDbInitializer
{
    /// <summary>Создаёт <c>%LocalAppData%\Rubrica\</c> при необходимости и выполняет <c>Database.Migrate()</c>.</summary>
    Task InitializeAsync(CancellationToken ct = default);
}
