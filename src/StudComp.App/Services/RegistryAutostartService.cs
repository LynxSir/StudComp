using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using StudComp.Infrastructure.Startup;

namespace StudComp.Services;

/// <summary>
/// Автозапуск через ключ реестра <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> —
/// без прав администратора (ARCHITECTURE §11.6).
/// </summary>
internal sealed class RegistryAutostartService(ILogger<RegistryAutostartService> logger) : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Rubrica";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
    }

    public void Enable()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            logger.LogWarning("Не удалось определить путь к исполняемому файлу — автозапуск не включён");
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, $"\"{exePath}\"");
        logger.LogInformation("Автозапуск включён: {Path}", exePath);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is null)
        {
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        logger.LogInformation("Автозапуск выключен");
    }
}
