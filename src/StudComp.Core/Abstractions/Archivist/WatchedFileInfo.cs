namespace StudComp.Core.Abstractions.Archivist;

/// <summary>
/// Снимок файла, обнаруженного в наблюдаемой папке, снятый после того, как файл признан стабильным.
/// Передаётся в <see cref="ISortingRuleEngine"/>, чтобы разбор правил оставался чистым и тестируемым —
/// сам движок к файловой системе не обращается (ARCHITECTURE §8.2, §8.4 п.1–2).
/// </summary>
/// <param name="FullPath">Абсолютный путь на момент обнаружения.</param>
/// <param name="FileName">Имя файла с расширением.</param>
/// <param name="Extension">Расширение с ведущей точкой, в нижнем регистре; пустая строка, если расширения нет.</param>
/// <param name="SizeBytes">Размер файла в байтах.</param>
/// <param name="CreatedAtUtc">Время создания по данным файловой системы.</param>
/// <param name="ModifiedAtUtc">Время последней записи по данным файловой системы.</param>
/// <param name="WatchedFolderPath">Корень наблюдаемой папки, в которой найден файл.</param>
public record WatchedFileInfo(
    string FullPath,
    string FileName,
    string Extension,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedAtUtc,
    string WatchedFolderPath);
