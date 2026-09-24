namespace StudComp.Core.Domain;

/// <summary>
/// Что за единица знания на карточке (new_addons.md §3.1). Влияет на иконку, на способ проверки по
/// умолчанию и на подсказку в форме — но не на структуру хранения.
/// </summary>
public enum CardKind
{
    /// <summary>Термин и его определение.</summary>
    Term = 0,

    /// <summary>Вопрос и ответ — основа пробного экзамена.</summary>
    Question = 1,

    /// <summary>Формула.</summary>
    Formula = 2,

    /// <summary>Кусок кода или сигнатура.</summary>
    Code = 3,

    /// <summary>Просто факт, который надо помнить.</summary>
    Fact = 4,
}

/// <summary>
/// Ручная пометка сложности (new_addons.md §3.1). Не то же самое, что накопленная статистика
/// ответов: это оценка автора карточки, а не результат тренировок.
/// </summary>
public enum CardDifficulty
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
}

/// <summary>
/// Карточка знания (new_addons.md §3.1) — один термин, формула, вопрос или факт. Привязки к
/// предмету, колоде и источникам необязательные и при исчезновении цели обнуляются: карточка это
/// написанный руками текст, терять его нельзя (тот же дух §14, что у <see cref="Note"/>).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
/// <remarks>
/// Состояние интервального повторения (<see cref="DueAt"/> и соседние поля) живёт прямо здесь, а не
/// в отдельной таблице: связь 1:1, и очередь дня читает эти поля в каждом запросе — join на горячем
/// пути не дал бы ничего взамен. Прецедент — поля прогноза на <see cref="Subject"/> (ADR §16.40).
/// </remarks>
public sealed class Card
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Предмет карточки; <see langword="null"/> — общая карточка вне предмета.</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>Колода; <see langword="null"/> — карточка лежит «россыпью» в предмете.</summary>
    public Guid? DeckId { get; set; }

    public CardKind Kind { get; set; }

    /// <summary>Лицевая сторона: термин или вопрос.</summary>
    public string Front { get; set; } = string.Empty;

    /// <summary>Оборот: определение или ответ, в Markdown.</summary>
    public string Back { get; set; } = string.Empty;

    /// <summary>Подсказка — показывается по запросу <b>до</b> ответа (new_addons.md §5.2).</summary>
    public string? Hint { get; set; }

    /// <summary>Откуда взято: «Лекция 12.09», «Демидович §4.2» — свободный текст.</summary>
    public string? Source { get; set; }

    /// <summary>Закреплённые идут в библиотеке первыми и всплывают в поиске.</summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Исключена из тренировок, но остаётся в поиске: «выучил намертво» либо «пока не проходили».
    /// </summary>
    public bool IsSuspended { get; set; }

    public CardDifficulty Difficulty { get; set; } = CardDifficulty.Normal;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Время последней правки — по нему сортируются недавние карточки.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Мягкое удаление (new_addons.md §3.5): карточка уходит из библиотеки, поиска и тренировок, но
    /// остаётся в «Корзине» с кнопкой «Вернуть». Физически строка удаляется только по явному
    /// «Очистить корзину» либо по ретеншну.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Заметка-источник, если карточка порождена из неё.</summary>
    public Guid? SourceNoteId { get; set; }

    /// <summary>Файл-источник, если карточка сделана к разложенному файлу.</summary>
    public Guid? SourceFileRecordId { get; set; }

    /// <summary>
    /// Когда карточку пора повторить. <see langword="null"/> — новая, ни разу не показанная
    /// (new_addons.md §3.2).
    /// </summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Текущий интервал повторения в днях; дробный, потому что «переучить» — это минуты.</summary>
    public double IntervalDays { get; set; }

    /// <summary>Коэффициент лёгкости SM-2; старт 2.5, пол 1.3 (new_addons.md §6.1).</summary>
    public double EaseFactor { get; set; } = 2.5;

    /// <summary>Сколько раз подряд карточку вспомнили. Провал сбрасывает счётчик в ноль.</summary>
    public int Repetitions { get; set; }

    /// <summary>Сколько раз карточку забывали за всю её жизнь — сырьё для «слабых мест».</summary>
    public int Lapses { get; set; }

    public DateTimeOffset? LastReviewedAt { get; set; }

    /// <summary>
    /// Ключ keyed-стратегии планирования повторений. Пусто — стратегия по умолчанию (SM-2).
    /// Задел под сменную стратегию по образцу <see cref="Subject.ForecastStrategyName"/>
    /// (new_addons.md §6.2).
    /// </summary>
    public string SchedulerName { get; set; } = string.Empty;
}
