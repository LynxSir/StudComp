namespace StudComp.Core.Domain;

/// <summary>
/// Единственное место, где считается «где лежит папка предмета» (new_addons.md §4). До Phase 13.2 эта
/// логика жила в трёх копиях с тремя разными санитизациями — <c>StudyWorkspace</c>, движок правил
/// Архивариуса и ручная сортировка «Неразобранного»; расхождение между ними было тихим и потому
/// особенно вредным.
/// </summary>
/// <remarks>
/// Чистая функция на голом BCL — в <c>Core/Domain</c> по той же причине, что
/// <see cref="WeekParityCalculator"/> и <see cref="TokenSimilarity"/>: потребителей несколько, а
/// прямые ссылки между <c>Modules.*</c> запрещены (ARCHITECTURE §5.1).
///
/// <para>
/// <b>Трактовка <see cref="Subject.FolderPath"/></b> (три случая, различимы без единого нового столбца
/// в БД, поэтому Phase 13.2 обошлась без миграции):
/// </para>
/// <list type="bullet">
///   <item>пусто — папка вычисляется от имени предмета внутри учебной папки;</item>
///   <item>относительное значение — имя подпапки внутри учебной папки (обычный случай с Phase 13.2);</item>
///   <item>абсолютный путь — «своя папка», работает как до Phase 13.2 и сильнее любых корней.</item>
/// </list>
/// </remarks>
public static class SubjectFolder
{
    /// <summary>Имя папки для предмета без названия — пустую строку в путь не подставить.</summary>
    private const string Fallback = "Без названия";

    /// <summary>
    /// Приводит название к безопасному имени папки: запрещённые символы (включая разделители пути)
    /// заменяются подчёркиванием, поэтому результат всегда ровно один сегмент.
    /// </summary>
    public static string Sanitize(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. trimmed.Select(ch => invalid.Contains(ch) ? '_' : ch)]);

        // Windows не хранит папки, чьё имя кончается точкой или пробелом.
        return cleaned.TrimEnd('.', ' ') is { Length: > 0 } result ? result : Fallback;
    }

    /// <summary>«Своя папка» — пользователь указал абсолютный путь, возможно вне учебной папки.</summary>
    public static bool IsCustomPath(string? folderPath) =>
        !string.IsNullOrWhiteSpace(folderPath) && Path.IsPathRooted(folderPath.Trim());

    /// <summary>
    /// Имя подпапки предмета внутри учебной папки: заданное пользователем, иначе — по названию
    /// предмета. Для «своей папки» возвращает имя её последнего сегмента — оно используется только
    /// как подпись в интерфейсе, путь берётся из <see cref="Resolve"/>.
    /// </summary>
    public static string FolderName(Subject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (IsCustomPath(subject.FolderPath))
        {
            var leaf = Path.GetFileName(subject.FolderPath.Trim().TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(leaf) ? Sanitize(subject.Name) : leaf;
        }

        return string.IsNullOrWhiteSpace(subject.FolderPath)
            ? Sanitize(subject.Name)
            : Sanitize(subject.FolderPath);
    }

    /// <summary>
    /// Абсолютный путь к папке предмета. Пустая строка — учебная папка не выбрана и своя не задана,
    /// то есть подставить нечего (вызывающий обязан это проверить, а не создавать папку наугад).
    /// </summary>
    public static string Resolve(string? studyRoot, Subject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (IsCustomPath(subject.FolderPath))
        {
            return subject.FolderPath.Trim();
        }

        var root = (studyRoot ?? string.Empty).Trim();
        return root.Length == 0 ? string.Empty : Path.Combine(root, FolderName(subject));
    }
}
