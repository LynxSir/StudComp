namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Файл, ожидающий разбора: путь плюс корень наблюдения, из которого он пришёл. Корень нужен явно —
/// вывести его из пути нельзя, когда наблюдаемых папок несколько и одна может лежать внутри другой
/// (ARCHITECTURE §8.7, Phase 10).
/// </summary>
public readonly record struct PendingFile(string WatchedFolder, string Path);

/// <summary>
/// Наблюдатель за папками (ARCHITECTURE §8.2). Объявлен здесь, а не в <c>Core.Abstractions</c>:
/// реализация наследует <c>IHostedService</c>, что притащило бы NuGet-пакеты хостинга в <c>Core</c>
/// (ADR §16.11).
/// </summary>
public interface IFileWatcherService
{
    /// <summary>Наблюдение включено и хотя бы одна настроенная папка реально существует.</summary>
    bool IsWatching { get; }

    /// <summary>Папки, за которыми ведётся наблюдение прямо сейчас; пусто, если ни одна не поднята.</summary>
    IReadOnlyList<string> WatchedFolders { get; }

    /// <summary>
    /// Все настроенные папки, включая временно недоступные на диске (внешний диск, сетевой путь) и
    /// учебную папку, если включено наблюдение за ней. Список для интерфейса — привязать правило
    /// можно и к папке, которой сейчас нет.
    /// </summary>
    IReadOnlyList<string> ConfiguredFolders { get; }

    /// <summary>
    /// Что-то изменилось в учёте файлов - вкладке «Неразобранное» пора перечитать список. Обычное
    /// .NET-событие вместо <c>IMessenger</c>: подписчик один и живёт в UI-слое, границу модулей это
    /// сообщение не пересекает (ARCHITECTURE §11.4, ADR §16.29).
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>Пересканировать все наблюдаемые папки прямо сейчас (кнопка в UI, смена настроек).</summary>
    void RequestRescan();

    /// <summary>
    /// Поставить в очередь файлы, которые нашёл сверщик: события по ним <c>FileSystemWatcher</c>
    /// потерял. Дальше они идут обычным конвейером — сверщик не дублирует ни движок правил, ни
    /// исполнителя операций (ADR §16.53).
    /// </summary>
    void EnqueueMissed(IReadOnlyList<PendingFile> files);
}
