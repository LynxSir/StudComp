using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Проверка, что файл дописан и не занят другим процессом (ARCHITECTURE §8.4 п.2): два замера размера
/// с интервалом должны совпасть, и файл должен открываться с <see cref="FileShare.None"/>. Пока это не
/// так — ждём до истечения таймаута, потом сдаёмся и оставляем файл в покое.
/// </summary>
internal sealed class FileStabilityChecker(
    IFileSystem fileSystem,
    IOptionsMonitor<ArchivistOptions> options,
    ILogger<FileStabilityChecker> logger) : IFileStabilityChecker
{
    public async Task<bool> WaitUntilStableAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        var probeInterval = TimeSpan.FromMilliseconds(Math.Max(50, options.CurrentValue.StabilityProbeIntervalMs));
        var deadline = DateTimeOffset.UtcNow + timeout;

        long? previousSize = null;

        while (!ct.IsCancellationRequested)
        {
            if (!fileSystem.FileExists(path))
            {
                // Файл унесли, пока мы ждали — это не наша забота и не потеря.
                logger.LogDebug("Файл исчез во время проверки стабильности: {Path}", path);
                return false;
            }

            long size;
            try
            {
                size = fileSystem.GetFileSize(path);
            }
            catch (IOException)
            {
                size = -1;
            }

            if (size >= 0 && previousSize == size && CanOpenExclusively(path))
            {
                return true;
            }

            previousSize = size;

            if (DateTimeOffset.UtcNow + probeInterval > deadline)
            {
                logger.LogInformation(
                    "Файл не стабилизировался за отведённое время, отложен до следующего скана: {Path}", path);
                return false;
            }

            await Task.Delay(probeInterval, ct).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Открытие с <c>FileShare.None</c> — самый честный способ узнать, держит ли файл кто-то ещё.</summary>
    private bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = fileSystem.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
