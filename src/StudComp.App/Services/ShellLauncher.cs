using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using StudComp.Data.Repositories;
using ActivityEntry = StudComp.Core.Domain.ActivityEntry;
using ActivityKind = StudComp.Core.Domain.ActivityKind;

namespace StudComp.Services;

/// <inheritdoc cref="IShellLauncher"/>
internal sealed class ShellLauncher(ILogger<ShellLauncher> logger, IActivityRepository activityRepository)
    : IShellLauncher
{
    public bool OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        if (!TryStart(new ProcessStartInfo(path) { UseShellExecute = true }, path))
        {
            return false;
        }

        LogOpened(path);
        return true;
    }

    public bool RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (File.Exists(path))
        {
            // Кавычки обязательны: без них Проводник спотыкается о пробелы в пути.
            return TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""), path);
        }

        var directory = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory)
               && Directory.Exists(directory)
               && TryStart(new ProcessStartInfo(directory) { UseShellExecute = true }, directory);
    }

    private bool TryStart(ProcessStartInfo startInfo, string path)
    {
        try
        {
            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(exception, "Не удалось открыть {Path}", path);
            return false;
        }
    }

    /// <summary>Пишет в ленту активности факт открытия файла — источник зоны «последние файлы» Дашборда.</summary>
    private void LogOpened(string path)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await activityRepository.AddAsync(new ActivityEntry
                {
                    Id = Guid.NewGuid(),
                    Kind = ActivityKind.FileOpened,
                    Timestamp = DateTimeOffset.UtcNow,
                    Path = path,
                    Title = Path.GetFileName(path),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось записать открытие файла в ленту активности: {Path}", path);
            }
        });
    }
}
