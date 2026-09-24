namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Сравнение путей файловой системы Windows: регистр не важен, хвостовой разделитель не важен.
/// Заведено с приходом нескольких наблюдаемых папок (Phase 10) — сопоставлять папку правила с
/// корнем наблюдения приходится в движке, сверщике и предпросмотре.
/// </summary>
internal static class PathComparison
{
    /// <summary>Указывают ли обе строки на одну и ту же папку.</summary>
    public static bool SameDirectory(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Лежит ли файл внутри папки. Хвостовой разделитель добавляется намеренно: без него
    /// «C:\Загрузки» считалась бы родителем «C:\Загрузки (старое)\файл.docx».
    /// </summary>
    public static bool Contains(string directory, string filePath)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var prefix = Normalize(directory) + Path.DirectorySeparatorChar;
        return NormalizeFull(filePath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ключ для словарей и множеств папок.</summary>
    public static string Normalize(string path) =>
        NormalizeFull(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizeFull(string path)
    {
        var trimmed = path.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Мусор в настройках не должен ронять конвейер — сравним как есть.
            return trimmed;
        }
    }
}
