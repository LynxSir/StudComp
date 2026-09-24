namespace StudComp.Infrastructure.Startup;

/// <summary>Найденное обновление приложения. Без типов Velopack — контракт остаётся в Infrastructure.</summary>
/// <param name="Version">Версия, до которой можно обновиться (SemVer).</param>
/// <param name="IsDelta">Будет ли применён delta-пакет (а не полный).</param>
public sealed record AppUpdateInfo(string Version, bool IsDelta);

/// <summary>
/// Проверка и применение обновлений через Velopack (ARCHITECTURE §13, §11.6). Единственный сетевой
/// вызов в приложении: асинхронный, с opt-out в Настройках, по умолчанию — приватный GitHub-репозиторий.
/// </summary>
/// <remarks>
/// Реализация живёт в <c>StudComp.App</c> (тянет пакет <c>Velopack</c>, как реестровый автозапуск —
/// в App из-за <c>net*-windows</c>). Если приложение запущено не из установленной копии
/// (<c>dotnet run</c>, распакованный zip) или не задан токен доступа —
/// <see cref="IsUpdateSupported"/> равно <c>false</c>, а <see cref="CheckAsync"/> возвращает <c>null</c>.
/// </remarks>
public interface IUpdateService
{
    /// <summary>Текущая установленная версия (или версия сборки, если копия не установлена).</summary>
    string CurrentVersion { get; }

    /// <summary>Доступно ли обновление в этой среде (установленная копия + задан фид/токен).</summary>
    bool IsUpdateSupported { get; }

    /// <summary>Спросить у фида, есть ли версия новее. <c>null</c> — обновлений нет или проверка недоступна.</summary>
    Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default);

    /// <summary>Скачать пакеты найденного обновления в локальный staging (не применяя).</summary>
    Task DownloadAsync(AppUpdateInfo update, IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>Применить скачанное обновление и перезапустить приложение. Возврата из метода нет.</summary>
    void ApplyAndRestart(AppUpdateInfo update);
}
