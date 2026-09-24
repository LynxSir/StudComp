namespace StudComp.Services;

/// <summary>
/// Открытие файлов и папок средствами Windows. Отдельный контракт, чтобы ViewModel'и не звали
/// <c>Process.Start</c> напрямую и оставались тестируемыми (тот же довод, что у ADR §16.24).
/// </summary>
public interface IShellLauncher
{
    /// <summary>Открыть файл приложением по умолчанию. <see langword="false"/> — файла нет или система отказала.</summary>
    bool OpenFile(string path);

    /// <summary>Показать файл в Проводнике, выделив его. Если файла нет — открыть папку.</summary>
    bool RevealInExplorer(string path);
}
