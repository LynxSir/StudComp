namespace StudComp.Core.Domain;

/// <summary>
/// Что случилось с файлами картинок между двумя состояниями текста заметки.
/// </summary>
/// <param name="Detached">Пути, на которые текст больше не ссылается ни разу.</param>
/// <param name="Reattached">Пути, ссылка на которые появилась снова.</param>
public readonly record struct ImageReferenceDelta(
    IReadOnlyList<string> Detached,
    IReadOnlyList<string> Reattached);

/// <summary>
/// Сверка ссылок на картинки: файл следует за <b>результатом</b> правки, а не за командой.
/// Перехватить <c>Ctrl+Z</c> нельзя, зато после отмены текст снова ссылается на файл — и одно
/// правило «ссылок стало ноль → убрать, ссылка вернулась → вернуть» закрывает разом удаление
/// токена, отмену, повтор, вырезание и вставку.
/// </summary>
public static class MarkdownImageReconciliation
{
    /// <summary>
    /// Сколько раз текст ссылается на каждую локальную картинку. Ключ — путь, приведённый к одному
    /// виду: регистр и вид разделителя для файла на диске значения не имеют.
    /// </summary>
    public static IReadOnlyDictionary<string, int> CountReferences(string? markdown)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in MarkdownLocalImages.Tokens(markdown))
        {
            if (!token.IsLocal)
            {
                continue;
            }

            var key = MarkdownImageEditing.Normalize(token.Path);
            if (key.Length == 0)
            {
                continue;
            }

            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    /// <summary>
    /// Что изменилось между двумя срезами. Отцепление считается только при падении счётчика до
    /// нуля — поэтому удаление одной из двух ссылок на один и тот же файл его не трогает.
    /// </summary>
    public static ImageReferenceDelta Compare(
        IReadOnlyDictionary<string, int>? before,
        IReadOnlyDictionary<string, int>? after)
    {
        var was = before ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var now = after ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var detached = new List<string>();
        var reattached = new List<string>();

        foreach (var (path, count) in was)
        {
            if (count > 0 && (!now.TryGetValue(path, out var current) || current == 0))
            {
                detached.Add(path);
            }
        }

        foreach (var (path, count) in now)
        {
            if (count > 0 && (!was.TryGetValue(path, out var previous) || previous == 0))
            {
                reattached.Add(path);
            }
        }

        return new ImageReferenceDelta(detached, reattached);
    }

    /// <summary>
    /// Дешёвая отсечка перед разбором: ни одной ссылки в тексте быть не может. Сверка зовётся на
    /// каждое изменение текста, и лишний скан заметки тут ни к чему.
    /// </summary>
    public static bool MightContainImages(string? markdown) =>
        markdown is { Length: > 0 } && markdown.Contains("](", StringComparison.Ordinal);
}
