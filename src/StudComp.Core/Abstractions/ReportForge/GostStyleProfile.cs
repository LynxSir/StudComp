namespace StudComp.Core.Abstractions.ReportForge;

/// <summary>Горизонтальное выравнивание абзаца.</summary>
public enum ParagraphAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
    Justify = 3,
}

/// <summary>Где печатается номер страницы.</summary>
public enum PageNumberPosition
{
    BottomCenter = 0,
    BottomRight = 1,
    TopCenter = 2,
    TopRight = 3,
}

/// <summary>Поля страницы в миллиметрах (ARCHITECTURE §10.4).</summary>
public record MarginsMm(double Left, double Right, double Top, double Bottom);

/// <summary>Оформление одного уровня заголовков (ARCHITECTURE §10.4).</summary>
/// <param name="Level">Уровень заголовка, начиная с 1.</param>
/// <param name="FontSizePt">Кегль в пунктах.</param>
/// <param name="Bold">Полужирный ли заголовок.</param>
/// <param name="PageBreakBefore">Начинать ли с новой страницы — для 1-го уровня по ГОСТ 7.32-2017 да.</param>
/// <param name="Alignment">Выравнивание заголовка.</param>
/// <param name="UpperCase">Переводить ли текст заголовка в верхний регистр при рендере.</param>
public record HeadingStyleRule(
    int Level,
    double FontSizePt,
    bool Bold,
    bool PageBreakBefore,
    ParagraphAlignment Alignment,
    bool UpperCase);

/// <summary>Нумерация страниц (ARCHITECTURE §10.4).</summary>
/// <param name="Enabled">Печатать ли номера вообще.</param>
/// <param name="Position">Куда ставить номер.</param>
/// <param name="SkipTitlePage">Оставлять ли титульный лист без номера (в общем счёте он при этом участвует).</param>
public record PageNumberingOptions(bool Enabled, PageNumberPosition Position, bool SkipTitlePage);

/// <summary>
/// Уровни заголовков, попадающие в оглавление. Само «делать ли оглавление» — свойство документа
/// (<see cref="ReportDocumentModel.GenerateTableOfContents"/>), а не профиля стиля.
/// </summary>
public record TocOptions(int MinLevel, int MaxLevel);

/// <summary>
/// Оформление блока листинга кода (ARCHITECTURE §10.5). Тоже данные профиля, а не константы в
/// <c>StyleDefinitionsFactory</c>: моноширинный шрифт и вид рамки у методичек тоже расходятся (ADR §16.5).
/// </summary>
/// <param name="MonospaceFontFamily">Моноширинный шрифт листингов (обычно Consolas).</param>
/// <param name="RelativeFontSizePt">
/// Поправка к основному кеглю: <c>-2</c> — листинг на 2 пт мельче текста. Итог не опускается ниже 8 пт.
/// </param>
/// <param name="Boxed">Рисовать ли рамку вокруг листинга.</param>
/// <param name="BackgroundHex">Цвет подложки (<c>RRGGBB</c> без решётки) либо <see langword="null"/> — без заливки.</param>
public record CodeBlockStyleRule(
    string MonospaceFontFamily,
    double RelativeFontSizePt,
    bool Boxed,
    string? BackgroundHex);

/// <summary>
/// Профиль форматирования, который применяет <see cref="IGostDocxRenderer"/>. Намеренно данные, а не
/// константы в коде: методички кафедр регулярно расходятся с базовым ГОСТ (чаще всего в полях и
/// титульном листе), и хардкод потребовал бы форка кода под каждый вуз (ADR §16.5).
/// Сериализуется в JSON и хранится в <c>REPORT_TEMPLATE.StyleProfileJson</c>; профиль по умолчанию —
/// <c>gost-7.32-2017.json</c> (ARCHITECTURE §10.4).
/// </summary>
/// <param name="BulletMarker">
/// Символ-маркер пунктов ненумерованного списка (в отчётах по ГОСТ 7.32-2017 обычно тире «–»).
/// Тоже данные, а не константа в рендерере — по той же причине, что и остальной профиль (ADR §16.32).
/// </param>
/// <param name="CodeBlock">
/// Оформление листингов кода. <see langword="null"/> в старых профилях из БД без этого ключа —
/// рендерер трактует его как заводское (Consolas, −2 пт, рамка, без заливки) (ADR §16.48).
/// </param>
public record GostStyleProfile(
    string FontFamily,
    double FontSizePt,
    double LineSpacing,
    MarginsMm Margins,
    double ParagraphIndentCm,
    ParagraphAlignment BodyAlignment,
    string BulletMarker,
    IReadOnlyList<HeadingStyleRule> HeadingRules,
    PageNumberingOptions PageNumbering,
    TocOptions TableOfContents,
    string? TitlePageTemplate,
    CodeBlockStyleRule? CodeBlock = null);
