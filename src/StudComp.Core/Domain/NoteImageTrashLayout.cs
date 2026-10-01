using System.Globalization;

namespace StudComp.Core.Domain;

/// <summary>
/// Раскладка скрытой корзины картинок заметок внутри учебной папки:
/// <c>&lt;Учебная папка&gt;/.studcomp/trash/&lt;id заметки&gt;/&lt;метка времени&gt;/&lt;путь как был&gt;</c>.
/// </summary>
/// <remarks>
/// Само дерево и есть опись: ни файла-описи, ни JSON — восстановление после падения приложения
/// это обход каталога. Метка времени живёт в имени папки, а не в атрибутах файла: перемещение
/// сохраняет исходное время записи, и по нему ничего не узнать.
/// <para>
/// Корзина лежит в учебной папке, а не в <c>%LocalAppData%</c>, намеренно: перемещение в пределах
/// одного тома мгновенно — именно это делает удаление картинки обратимым бесплатно.
/// </para>
/// </remarks>
public static class NoteImageTrashLayout
{
    /// <summary>Служебная папка приложения внутри учебной папки.</summary>
    public const string ServiceFolder = ".studcomp";

    /// <summary>Подпапка корзины внутри служебной.</summary>
    public const string TrashFolder = "trash";

    /// <summary>
    /// Формат метки времени в имени папки сессии: сортируется как строка, пишется и читается
    /// строго в UTC — иначе смена часового пояса или перевод часов сделали бы «старше суток»
    /// неопределённым.
    /// </summary>
    private const string StampFormat = "yyyyMMddHHmmssfff";

    /// <summary>Корень корзины.</summary>
    public static string TrashRoot(string studyRoot) =>
        Path.Combine(studyRoot, ServiceFolder, TrashFolder);

    /// <summary>Папка одной заметки в корзине — в ней лежат папки сессий.</summary>
    public static string NoteDirectory(string studyRoot, Guid noteId) =>
        Path.Combine(TrashRoot(studyRoot), noteId.ToString("N", CultureInfo.InvariantCulture));

    /// <summary>Папка одной сессии правки заметки.</summary>
    public static string SessionDirectory(string studyRoot, Guid noteId, DateTimeOffset stamp) =>
        Path.Combine(
            NoteDirectory(studyRoot, noteId),
            stamp.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture));

    /// <summary>
    /// Куда переедет картинка. Путь внутри корзины зеркалит путь в учебной папке, поэтому
    /// возврат — это обратное перемещение без всякой описи. <see langword="null"/> — путь
    /// абсолютный либо выводит за пределы корзины (<c>..</c>), и трогать его нельзя.
    /// </summary>
    public static string? StagePathFor(string studyRoot, Guid noteId, DateTimeOffset stamp, string relativeImagePath)
    {
        if (string.IsNullOrWhiteSpace(studyRoot)
            || string.IsNullOrWhiteSpace(relativeImagePath)
            || Path.IsPathRooted(relativeImagePath))
        {
            return null;
        }

        var session = SessionDirectory(studyRoot, noteId, stamp);

        try
        {
            var full = Path.GetFullPath(Path.Combine(session, relativeImagePath));
            var root = WithSeparator(Path.GetFullPath(session));

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Разобрать путь в корзине обратно: чья заметка, когда убрали и что это был за файл.</summary>
    public static bool TryParse(
        string studyRoot,
        string stagedAbsolutePath,
        out Guid noteId,
        out DateTimeOffset stagedAt,
        out string relativeImagePath)
    {
        noteId = Guid.Empty;
        stagedAt = default;
        relativeImagePath = string.Empty;

        if (string.IsNullOrWhiteSpace(studyRoot) || string.IsNullOrWhiteSpace(stagedAbsolutePath))
        {
            return false;
        }

        string full;
        string root;

        try
        {
            full = Path.GetFullPath(stagedAbsolutePath);
            root = WithSeparator(Path.GetFullPath(TrashRoot(studyRoot)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3
            || !Guid.TryParseExact(parts[0], "N", out noteId)
            || !DateTimeOffset.TryParseExact(
                parts[1],
                StampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out stagedAt))
        {
            noteId = Guid.Empty;
            stagedAt = default;
            return false;
        }

        relativeImagePath = string.Join('/', parts[2..]);
        return relativeImagePath.Length > 0;
    }

    /// <summary>Лежит ли путь внутри корзины — служебные файлы не показываются и не сортируются.</summary>
    public static bool IsInsideTrash(string studyRoot, string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(studyRoot) || string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        try
        {
            return WithSeparator(Path.GetFullPath(absolutePath))
                .StartsWith(WithSeparator(Path.GetFullPath(TrashRoot(studyRoot))), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
