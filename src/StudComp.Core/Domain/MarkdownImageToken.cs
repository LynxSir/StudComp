namespace StudComp.Core.Domain;

/// <summary>
/// Вставка картинки в размеченном тексте как один неделимый кусок: <c>![подпись](&lt;путь&gt;){width=N}</c>.
/// В редакторе заметки она ведёт себя как единый объект, а не как набор символов, поэтому её границы
/// нужны отдельным понятием (new_addons.md §12).
/// </summary>
/// <param name="Start">Индекс восклицательного знака в исходном тексте.</param>
/// <param name="Length">Длина вместе с хвостовым блоком атрибутов <c>{…}</c>, если он есть.</param>
/// <param name="LinkLength">
/// Длина без блока атрибутов — ровно то, что видит Markdig и что лежит в
/// <c>ImageBlock.SourceLength</c>. Нужна, чтобы сопоставить токен с блоком из разбора.
/// </param>
/// <param name="Path">Раскодированный путь (проценты уже сняты).</param>
/// <param name="IsLocal">Файл на диске, а не внешний адрес — только такие мы переносим и удаляем.</param>
public readonly record struct MarkdownImageToken(
    int Start,
    int Length,
    int LinkLength,
    string Path,
    bool IsLocal)
{
    /// <summary>Позиция сразу за токеном.</summary>
    public int End => Start + Length;

    /// <summary>Позиция сразу за ссылкой, до блока атрибутов.</summary>
    public int LinkEnd => Start + LinkLength;

    /// <summary>Есть ли хвостовой блок атрибутов <c>{…}</c>.</summary>
    public bool HasAttributes => Length > LinkLength;

    /// <summary>Позиция строго внутри токена — каретке тут стоять нельзя.</summary>
    public bool Contains(int position) => position > Start && position < End;

    /// <summary>Позиция на одной из границ токена.</summary>
    public bool IsBoundary(int position) => position == Start || position == End;
}
