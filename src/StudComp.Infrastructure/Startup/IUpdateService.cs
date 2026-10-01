namespace StudComp.Infrastructure.Startup;

/// <summary>Найденное обновление приложения. Без типов Velopack — контракт остаётся в Infrastructure.</summary>
/// <param name="Version">Версия, до которой можно обновиться (SemVer).</param>
/// <param name="IsDelta">Будет ли применён delta-пакет (а не полный).</param>
/// <param name="ReleaseNotes">Пользовательское описание изменений.</param>
/// <param name="DownloadSizeBytes">Ожидаемый размер загрузки в байтах.</param>
public sealed record AppUpdateInfo(
    string Version,
    bool IsDelta,
    string ReleaseNotes,
    long DownloadSizeBytes);

/// <summary>
/// Проверка и применение обновлений через Velopack. Единственный сетевой вызов в приложении:
/// асинхронный, с opt-out в Настройках, из публичных GitHub Releases.
/// </summary>
/// <remarks>
/// Реализация живёт в <c>StudComp.App</c> (тянет пакет <c>Velopack</c>, как реестровый автозапуск —
/// в App из-за <c>net*-windows</c>). Если приложение запущено не из установленной копии
/// (<c>dotnet run</c> или обычный распакованный каталог) —
/// <see cref="IsUpdateSupported"/> равно <c>false</c>, а <see cref="CheckAsync"/> возвращает <c>null</c>.
/// </remarks>
public interface IUpdateService
{
    /// <summary>Текущая установленная версия (или версия сборки, если копия не установлена).</summary>
    string CurrentVersion { get; }

    /// <summary>Доступно ли обновление в этой среде (установленная копия + задан репозиторий).</summary>
    bool IsUpdateSupported { get; }

    /// <summary>Спросить у фида, есть ли версия новее. <c>null</c> — обновлений нет или проверка недоступна.</summary>
    Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default);

    /// <summary>Скачать пакеты найденного обновления в локальный staging (не применяя).</summary>
    Task DownloadAsync(AppUpdateInfo update, IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>Пометить скачанное обновление для тихой установки при штатном выходе.</summary>
    void RequestApplyOnExit(AppUpdateInfo update);

    /// <summary>
    /// Если обновление помечено, запустить внешний updater. Он дождётся завершения процесса,
    /// тихо заменит файлы и снова откроет приложение.
    /// </summary>
    void ApplyPendingUpdateOnExit();
}
