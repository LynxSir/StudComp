namespace StudComp.Core.Abstractions.ReportForge;

/// <summary>
/// Начертание <see cref="InlineRun"/>. Флаги — потому что в Markdown выделения вкладываются друг в друга.
/// </summary>
[Flags]
public enum InlineStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,

    /// <summary>Код внутри строки — рендерится моноширинным (ARCHITECTURE §10.5).</summary>
    Code = 16,

    /// <summary>
    /// Формула <c>$…$</c>: в <see cref="InlineRun.Text"/> лежит исходник TeX, а не готовый текст.
    /// Предпросмотр разбирает его и верстает (new_addons.md §11.1), генератор .docx выводит
    /// исходником — полноценный OMML остаётся в бэклоге (ARCHITECTURE §18).
    /// </summary>
    Math = 32,
}

/// <summary>
/// Перенос строки внутри абзаца. Мягкий — одиночный <c>Enter</c> в исходнике, жёсткий — два
/// пробела на конце строки или <c>\</c> (CommonMark).
/// </summary>
public enum InlineBreak
{
    None = 0,
    Soft = 1,
    Hard = 2,
}

/// <summary>
/// Кусок текста с одним начертанием — наименьшая единица, которую рендерер кладёт в <c>w:r</c>.
/// </summary>
/// <param name="Text">Сам текст; для переноса строки пустой.</param>
/// <param name="Style">Начертание, общее для всего куска.</param>
/// <param name="Hyperlink">URL, если это ссылка; иначе <see langword="null"/>.</param>
/// <param name="Break">
/// Если не <see cref="InlineBreak.None"/> — кусок означает перенос строки, а не текст.
/// Предпросмотр заметки переносит и мягкий, и жёсткий (иначе одиночный <c>Enter</c> пропадает —
/// new_addons.md §11.7), генератор .docx — только жёсткий, как велит CommonMark.
/// </param>
public record InlineRun(
    string Text,
    InlineStyle Style = InlineStyle.None,
    string? Hyperlink = null,
    InlineBreak Break = InlineBreak.None);

/// <summary>
/// Данные титульного листа, подтягиваются из <c>Subject</c> и <c>UserProfileSettings</c> (ARCHITECTURE §10.5).
/// </summary>
public record TitlePageInfo(
    string University,
    string? Faculty,
    string? Department,
    string WorkType,
    string? SubjectName,
    string StudentName,
    string? StudentGroup,
    string? SupervisorName,
    string? City,
    int? Year);

/// <summary>
/// Промежуточное дерево документа между парсером Markdown и рендерером DOCX, не знающее ни о том, ни о другом.
/// Эта прослойка обязательна: она развязывает парсинг и ГОСТ-рендеринг и позволит позже добавить второй
/// источник контента, не переписывая рендерер (ADR §16.4).
/// </summary>
/// <param name="TitlePage">Данные титульного листа либо <see langword="null"/>, чтобы его не делать.</param>
/// <param name="Blocks">Содержимое документа в порядке следования.</param>
/// <param name="GenerateTableOfContents">Вставлять ли поле оглавления (ADR §16.6).</param>
public record ReportDocumentModel(
    TitlePageInfo? TitlePage,
    IReadOnlyList<IReportBlock> Blocks,
    bool GenerateTableOfContents);

/// <summary>Маркер блочного элемента <see cref="ReportDocumentModel"/>.</summary>
public interface IReportBlock { }

/// <summary>Заголовок раздела. Уровни 1–3 попадают в автооглавление (ARCHITECTURE §10.4).</summary>
public record HeadingBlock(int Level, string Text) : IReportBlock;

/// <summary>Абзац основного текста, собранный из кусков с начертанием.</summary>
public record ParagraphBlock(IReadOnlyList<InlineRun> Runs) : IReportBlock;

/// <summary>Маркированный или нумерованный список; каждый пункт — вложенная модель, поэтому в пункте может быть что угодно.</summary>
public record ListBlock(bool Ordered, IReadOnlyList<ReportDocumentModel> Items) : IReportBlock;

/// <summary>
/// Таблица. Номер в подписи «Таблица N — …» рендерер проставляет сам, название берёт из
/// <paramref name="Caption"/> (ADR §16.31); без названия печатается просто «Таблица N».
/// </summary>
/// <param name="Headers">Ячейки строки заголовка.</param>
/// <param name="Rows">Строки тела таблицы, по ячейке на столбец.</param>
/// <param name="Caption">Название таблицы без слова «Таблица» и номера, либо <see langword="null"/>.</param>
public record TableBlock(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? Caption = null) : IReportBlock;

/// <summary>Листинг кода: моноширинный шрифт и рамка вместо ГОСТ-типографики абзаца (ARCHITECTURE §10.5).</summary>
public record CodeBlock(string? Language, string Code) : IReportBlock;

/// <summary>Изображение с автоподписью «Рисунок N — …», масштабируется под ширину текстового блока (ARCHITECTURE §10.5).</summary>
public record ImageBlock(string PathOrBase64, string? Caption) : IReportBlock
{
    public int? Width { get; init; }
    public int SourceStart { get; init; } = -1;
    public int SourceLength { get; init; }
}
