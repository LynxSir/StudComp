namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Временная папка на один тест с гарантированной уборкой. Все файловые тесты комбайна отчётов работают
/// только внутри неё (ARCHITECTURE §12).
/// </summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder(string prefix)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rubrica-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Создать файл с заданным содержимым и вернуть полный путь.</summary>
    public string WriteFile(string name, string content)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Тест мог оставить залоченный файл — временную папку подчистит система.
        }
    }
}
