using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Startup;
using Velopack;
using Velopack.Sources;
using UpdateOptions = StudComp.Infrastructure.Settings.UpdateOptions;

namespace StudComp.Services;

/// <summary>
/// <see cref="IUpdateService"/> поверх Velopack <see cref="UpdateManager"/> и приватного
/// GitHub-репозитория (ARCHITECTURE §13). Живёт в <c>App</c>, потому что тянет пакет
/// <c>Velopack</c> — по той же логике, что и реестровый автозапуск (ADR §16.16).
/// </summary>
/// <remarks>
/// <see cref="UpdateManager"/> строится лениво: под F5 / из распакованного zip копия не
/// «установлена», токен не задан, и вся служба схлопывается в no-op
/// (<see cref="IsUpdateSupported"/> = <c>false</c>).
/// </remarks>
internal sealed class UpdateService(
    IOptionsMonitor<UpdateOptions> options,
    ILogger<UpdateService> logger) : IUpdateService
{
    private UpdateManager? _manager;
    private bool _probed;
    private UpdateInfo? _pendingUpdate;

    public string CurrentVersion
    {
        get
        {
            var fromManager = TryGetManager()?.CurrentVersion?.ToString();
            if (!string.IsNullOrEmpty(fromManager))
            {
                return fromManager;
            }

            // Копия не установлена — показываем версию сборки.
            return Assembly.GetEntryAssembly()?
                       .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                       .InformationalVersion?.Split('+')[0]
                   ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
                   ?? "0.0.0";
        }
    }

    public bool IsUpdateSupported
    {
        get
        {
            var opt = options.CurrentValue;
            return TryGetManager() is { IsInstalled: true }
                   && !string.IsNullOrWhiteSpace(opt.GithubRepoUrl)
                   && !string.IsNullOrWhiteSpace(opt.GithubToken);
        }
    }

    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (!IsUpdateSupported)
        {
            logger.LogDebug("Проверка обновлений недоступна: копия не установлена или не задан токен");
            return null;
        }

        var manager = TryGetManager()!;
        var info = await manager.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
        if (info is null)
        {
            logger.LogInformation("Обновлений нет, установлена последняя версия");
            return null;
        }

        _pendingUpdate = info;
        var isDelta = info.DeltasToTarget is { Length: > 0 } && info.BaseRelease is not null;
        var version = info.TargetFullRelease.Version.ToString();
        logger.LogInformation("Найдено обновление до {Version} (delta: {IsDelta})", version, isDelta);
        return new AppUpdateInfo(version, isDelta);
    }

    public async Task DownloadAsync(
        AppUpdateInfo update, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var manager = TryGetManager()
                      ?? throw new InvalidOperationException("Обновление недоступно в этой среде");

        var info = _pendingUpdate
                   ?? await manager.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Обновление больше не доступно на сервере");

        await manager.DownloadUpdatesAsync(
            info,
            progress is null ? null : progress.Report,
            ct).ConfigureAwait(false);

        _pendingUpdate = info;
        logger.LogInformation("Обновление до {Version} скачано и готово к установке", update.Version);
    }

    public void ApplyAndRestart(AppUpdateInfo update)
    {
        var manager = TryGetManager()
                      ?? throw new InvalidOperationException("Обновление недоступно в этой среде");
        var info = _pendingUpdate
                   ?? throw new InvalidOperationException("Обновление не было скачано");

        logger.LogInformation("Применяю обновление до {Version} и перезапускаю", update.Version);
        manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
    }

    private UpdateManager? TryGetManager()
    {
        if (_probed)
        {
            return _manager;
        }

        _probed = true;
        try
        {
            var opt = options.CurrentValue;
            var source = new GithubSource(
                opt.GithubRepoUrl,
                string.IsNullOrWhiteSpace(opt.GithubToken) ? null : opt.GithubToken,
                opt.IncludePrerelease);
            _manager = new UpdateManager(source);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось инициализировать Velopack UpdateManager");
            _manager = null;
        }

        return _manager;
    }
}
