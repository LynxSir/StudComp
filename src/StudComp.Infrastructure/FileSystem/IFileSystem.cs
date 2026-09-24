namespace StudComp.Infrastructure.FileSystem;

/// <summary>
/// Тонкая обёртка над <see cref="System.IO"/> — чтобы файловые операции Архивариуса (Phase 5)
/// можно было тестировать без реального диска (ARCHITECTURE §5, «FileSystem abstraction»).
/// </summary>
/// <remarks>
/// Метода удаления здесь нет и не будет: ни архивариус, ни импорт файлов в учебную папку не удаляют
/// пользовательские файлы, и отсутствие операции в абстракции — самая дешёвая гарантия этого
/// (ARCHITECTURE §8.6, §14, ADR §16.30). Перемещение и копирование всегда зовутся с
/// <c>overwrite: false</c> — существующий файл не перезаписывается ни при каких условиях.
/// Межтомовое перемещение отдано <c>File.Move</c>: он копирует и удаляет исходник средствами ОС,
/// причём удаляет только после успешного копирования — свой copy+verify+delete был бы лишь ещё
/// одним шансом потерять файл.
/// </remarks>
public interface IFileSystem
{
    /// <summary>Существует ли файл по пути.</summary>
    bool FileExists(string path);

    /// <summary>Существует ли каталог по пути.</summary>
    bool DirectoryExists(string path);

    /// <summary>Создаёт каталог (со всеми промежуточными), если его ещё нет.</summary>
    void CreateDirectory(string path);

    /// <summary>Открывает файл на чтение (<see cref="FileShare.Read"/>).</summary>
    Stream OpenRead(string path);

    /// <summary>Открывает файл с явно заданными режимом/доступом/разделением.</summary>
    FileStream Open(string path, FileMode mode, FileAccess access, FileShare share);

    /// <summary>Перемещает/переименовывает файл. <paramref name="overwrite"/> по умолчанию выключен —
    /// Архивариус не перезаписывает пользовательские файлы (ARCHITECTURE §8.6, §14).</summary>
    void Move(string sourcePath, string destinationPath, bool overwrite = false);

    /// <summary>Копирует файл. <paramref name="overwrite"/> по умолчанию выключен — существующий
    /// файл не перезаписывается ни при каких условиях (ARCHITECTURE §14).</summary>
    void Copy(string sourcePath, string destinationPath, bool overwrite = false);

    /// <summary>Размер файла в байтах.</summary>
    long GetFileSize(string path);

    /// <summary>Время последней записи в файл в UTC.</summary>
    DateTimeOffset GetLastWriteTimeUtc(string path);

    DateTimeOffset GetCreationTimeUtc(string path);

    /// <summary>Атрибуты файла — нужны, чтобы отличать скрытые/системные файлы от пользовательских.</summary>
    FileAttributes GetAttributes(string path);

    /// <summary>Перечисляет файлы в каталоге по маске (без рекурсии по умолчанию).</summary>
    IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly);

    /// <summary>Перечисляет подкаталоги (без рекурсии по умолчанию) — дерево папки предмета в Хабе
    /// и раскрытие перетащенных папок при импорте.</summary>
    IEnumerable<string> EnumerateDirectories(string directoryPath, string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly);
}
