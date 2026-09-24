using System.IO.Enumeration;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Фильтрация шума перед разбором файла (ARCHITECTURE §8.4 п.1): маски игнор-листа плюс эвристика
/// «мелкий и только что созданный — скорее всего, ещё пишется».
/// </summary>
internal static class IgnoreListMatcher
{
    // Оба атрибута — Windows-специфичные, в BCL-перечислении FileAttributes их нет, поэтому значения
    // прописаны числом (winnt.h): FILE_ATTRIBUTE_RECALL_ON_OPEN и FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS.
    // Именно ими OneDrive и «Файлы по запросу» помечают ещё не скачанные заглушки.
    internal const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    internal const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>Совпадает ли имя файла хотя бы с одной маской игнор-листа (регистр не важен).</summary>
    public static bool IsIgnoredName(string fileName, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Файл мельче порога и создан только что — почти наверняка его ещё дописывают. Пропускаем этот
    /// заход, файл вернётся к нам следующим событием или сканом.
    /// </summary>
    public static bool IsTooFreshAndSmall(
        long sizeBytes,
        DateTimeOffset createdAtUtc,
        DateTimeOffset nowUtc,
        ArchivistOptions options) =>
        sizeBytes < options.MinFileSizeBytes
        && nowUtc - createdAtUtc < TimeSpan.FromSeconds(options.MinFileAgeSeconds);

    /// <summary>Скрытые и системные файлы пользователь в наблюдаемую папку не кладёт осознанно.</summary>
    public static bool IsSystemOrHidden(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.Hidden)
        || attributes.HasFlag(FileAttributes.System)
        || attributes.HasFlag(FileAttributes.Directory);

    /// <summary>
    /// Файл-«заглушка» облачного хранилища: на диске лежит только метаинформация, содержимое качается
    /// при первом чтении (ARCHITECTURE §8.5). Такие файлы пропускаем целиком — иначе хэшер сам
    /// спровоцирует загрузку, а перемещать недокачанный файл тем более нельзя. Файл вернётся к нам
    /// сам, когда OneDrive его материализует: ближайшим reconciliation-проходом.
    /// </summary>
    /// <remarks>
    /// <c>Offline</c>/<c>RecallOnOpen</c>/<c>RecallOnDataAccess</c> — собственно placeholder'ы.
    /// <c>ReparsePoint</c> добавлен намеренно: это символические ссылки и junction'ы, двигать которые
    /// архивариусу тоже нечего.
    /// </remarks>
    public static bool IsCloudPlaceholder(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.Offline)
        || attributes.HasFlag(RecallOnOpen)
        || attributes.HasFlag(RecallOnDataAccess)
        || attributes.HasFlag(FileAttributes.ReparsePoint);
}
