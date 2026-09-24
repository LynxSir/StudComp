using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>Проверка стабильности файла на реальных временных файлах (ARCHITECTURE §8.4 п.2, §12).</summary>
public sealed class FileStabilityCheckerTests : IDisposable
{
    private readonly TempFolder _folder = new("stability");
    private readonly TestOptionsMonitor<ArchivistOptions> _options =
        new(new ArchivistOptions { StabilityProbeIntervalMs = 50 });

    private FileStabilityChecker CreateChecker() => new(
        new SystemFileSystem(),
        _options,
        NullLogger<FileStabilityChecker>.Instance);

    [Fact]
    public async Task Settled_file_is_stable()
    {
        var path = _folder.WriteFile("готово.txt", "содержимое");

        var stable = await CreateChecker().WaitUntilStableAsync(path, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(stable);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Locked_file_is_not_stable_within_timeout()
    {
        var path = _folder.WriteFile("занят.txt", "содержимое");

        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var stable = await CreateChecker().WaitUntilStableAsync(path, TimeSpan.FromMilliseconds(400), CancellationToken.None);

        Assert.False(stable);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Growing_file_becomes_stable_once_writing_stops()
    {
        var path = _folder.WriteFile("растёт.txt", "н");

        using var cts = new CancellationTokenSource();
        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                await File.AppendAllTextAsync(path, new string('x', 1024), cts.Token);
                await Task.Delay(60, cts.Token);
            }
        }, cts.Token);

        var stable = await CreateChecker().WaitUntilStableAsync(path, TimeSpan.FromSeconds(10), CancellationToken.None);
        await writer;

        Assert.True(stable);
    }

    [Fact]
    public async Task Missing_file_is_not_stable()
    {
        var stable = await CreateChecker()
            .WaitUntilStableAsync(_folder.Combine("нет-такого.txt"), TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(stable);
    }

    public void Dispose() => _folder.Dispose();
}
