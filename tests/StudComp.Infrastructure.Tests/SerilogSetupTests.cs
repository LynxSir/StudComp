using Microsoft.Extensions.Configuration;
using Serilog;
using StudComp.Infrastructure.Logging;

namespace StudComp.Infrastructure.Tests;

/// <summary>
/// <see cref="SerilogSetup"/> должен завести файловый sink в указанном каталоге и реально писать в него.
/// </summary>
public sealed class SerilogSetupTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "rubrica-logs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Configure_writes_rolling_log_file_into_target_directory()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var loggerConfiguration = new LoggerConfiguration();
        SerilogSetup.Configure(loggerConfiguration, configuration, _tempDir);

        var logger = loggerConfiguration.CreateLogger();
        logger.Information("проверочная запись {Marker}", "phase3");
        Log.CloseAndFlush();      // на случай, если статический логгер перехвачен
        (logger as IDisposable)?.Dispose();

        var files = Directory.GetFiles(_tempDir, "log-*.txt");
        Assert.NotEmpty(files);
        Assert.Contains("проверочная запись", File.ReadAllText(files[0]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
