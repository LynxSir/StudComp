namespace StudComp.Infrastructure.Startup;

/// <summary>
/// Управляет автозапуском приложения вместе с Windows (ARCHITECTURE §11.5, §11.6). Реализуется без
/// прав администратора — через ключ реестра <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// </summary>
/// <remarks>Реестровая реализация живёт в <c>StudComp.App</c> (TFM <c>net*-windows</c>).</remarks>
public interface IAutostartService
{
    /// <summary>Прописан ли автозапуск в реестре сейчас.</summary>
    bool IsEnabled { get; }

    /// <summary>Включить автозапуск (записать путь к текущему исполняемому файлу).</summary>
    void Enable();

    /// <summary>Выключить автозапуск (удалить запись).</summary>
    void Disable();
}
