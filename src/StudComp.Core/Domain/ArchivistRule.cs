namespace StudComp.Core.Domain;

/// <summary>
/// Как <c>ISortingRuleEngine</c> сопоставляет файл с <see cref="ArchivistRule.Pattern"/> (ARCHITECTURE §8.4 п.3).
/// </summary>
public enum RuleMatchType
{
    /// <summary>Совпадение по расширению — низкоприоритетная «последняя линия обороны».</summary>
    Extension = 0,

    /// <summary>Вхождение подстроки в имя файла (опционально — в первые N КБ текста).</summary>
    Keyword = 1,

    /// <summary>Пользовательское регулярное выражение по полному имени файла (с расширением).</summary>
    Regex = 2,
}

/// <summary>
/// Правило сортировки архивариуса (ARCHITECTURE §7.1 <c>ARCHIVIST_RULE</c>).
/// POCO, ничего не знающий о персистентности: маппится из <c>StudComp.Data</c> через
/// <c>IEntityTypeConfiguration&lt;T&gt;</c>, без EF-атрибутов и навигационных свойств (ADR §16.10).
/// </summary>
public sealed class ArchivistRule
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Предмет, в который сортирует правило, либо <see langword="null"/> для общего правила.</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>Расширение, ключевое слово или регулярное выражение — трактуется по <see cref="MatchType"/>.</summary>
    public string Pattern { get; set; } = string.Empty;

    public RuleMatchType MatchType { get; set; }

    /// <summary>Чем больше, тем раньше проверяется; побеждает первое совпадение (ARCHITECTURE §8.4 п.3).</summary>
    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Тип работы для токена <c>{Type}</c> в шаблоне имени (напр. «ЛР», «КР», «Лекция»).
    /// Пусто — <c>{Type}</c> падает на ключевое слово правила либо расширение (ARCHITECTURE §8.4 п.4, ADR §16.28).
    /// </summary>
    public string? WorkType { get; set; }

    /// <summary>
    /// Наблюдаемая папка, в которой действует правило. <see langword="null"/> — правило работает во
    /// всех наблюдаемых папках (ARCHITECTURE §8.7, ADR §16.54).
    /// </summary>
    public string? WatchedFolder { get; set; }

    /// <summary>
    /// Шаблон целевого имени с токенами <c>{Subject}</c>, <c>{Type}</c>, <c>{Date:yyyyMMdd}</c>,
    /// <c>{OriginalName}</c>, <c>{Counter}</c>, <c>{Ext}</c> (ARCHITECTURE §8.4 п.4).
    /// </summary>
    public string RenameTemplate { get; set; } = string.Empty;
}
