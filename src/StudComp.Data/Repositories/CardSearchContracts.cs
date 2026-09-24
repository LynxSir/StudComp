using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Одна строка выдачи поиска (new_addons.md §4.5).</summary>
/// <param name="CardId">Найденная карточка.</param>
/// <param name="Score">Итоговый балл: <b>меньше — релевантнее</b>, как у <c>bm25</c> в SQLite.</param>
/// <param name="Snippet">
/// Отрывок оборота с маркерами <see cref="CardSearchHit.HighlightStart"/> и
/// <see cref="CardSearchHit.HighlightEnd"/> вокруг совпадений. Пусто, если отрывка нет.
/// </param>
public sealed record CardSearchHit(Guid CardId, double Score, string Snippet)
{
    /// <summary>
    /// Маркер начала совпадения. Управляющий символ, а не разметка: в тексте карточки его не бывает,
    /// и вьюмодели остаётся разрезать строку по маркерам в последовательность <c>Run</c>'ов —
    /// никакого HTML (new_addons.md §4.5).
    /// </summary>
    public const string HighlightStart = CardHighlight.MarkerStart;

    /// <summary>Маркер конца совпадения.</summary>
    public const string HighlightEnd = CardHighlight.MarkerEnd;
}

/// <summary>Параметры одного поискового запроса.</summary>
/// <param name="Limit">Сколько строк вернуть. Экран физически не покажет больше полусотни.</param>
/// <param name="Offset">Смещение для кнопки «показать ещё».</param>
/// <param name="ActiveSubjectId">
/// Предмет, по которому сейчас идёт пара (<c>IActiveSubjectProvider</c> живёт в App и передаёт его
/// сюда параметром). Его карточки получают прибавку к релевантности — тот самый штрих, ради
/// которого активный предмет вообще заводился (new_addons.md §4.5).
/// </param>
/// <param name="Sort">Порядок выдачи; по умолчанию — релевантность.</param>
public sealed record CardSearchOptions(
    int Limit = 50,
    int Offset = 0,
    Guid? ActiveSubjectId = null,
    CardSortOrder Sort = CardSortOrder.Relevance);

/// <summary>
/// Поиск по картотеке (new_addons.md §4). Единственное место в проекте с сырым SQL и
/// <c>MATCH</c> — модуль знает этот интерфейс, а не SQLite (new_addons.md §10.3).
/// </summary>
public interface ICardSearchRepository
{
    /// <summary>
    /// Найти карточки. Пустой запрос отдаёт недавние и закреплённые — «пустота» на экране это
    /// всегда чей-то недосмотр, а не результат.
    /// </summary>
    Task<IReadOnlyList<CardSearchHit>> SearchAsync(
        CardQuerySpec spec,
        CardSearchOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Работает ли полнотекстовый индекс. <see langword="false"/> — поиск идёт в деградированном
    /// режиме (new_addons.md §4.3), о чём честно сообщается в настройках раздела.
    /// </summary>
    Task<bool> IsFullTextAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Перестроить индекс целиком. Кнопка в настройках, а заодно лекарство от единственного
    /// сценария рассинхронизации — миграции, пересобравшей таблицу вместе с триггерами.
    /// Возвращает, сколько карточек проиндексировано.
    /// </summary>
    Task<int> RebuildAsync(CancellationToken ct = default);
}

/// <summary>
/// Порядок выдачи библиотеки (new_addons.md §8.2) — переключатель в шапке раздела.
/// </summary>
/// <remarks>
/// Порядок считает база, а не вьюмодель: сортировать нужно всю выборку, а на экране лежит только
/// её первая страница, и клиентская сортировка молча врала бы на второй.
/// </remarks>
public enum CardSortOrder
{
    /// <summary>По релевантности — ранжирование индекса с бустерами §4.5. Значение по умолчанию.</summary>
    Relevance = 0,

    /// <summary>Сначала недавно изменённые.</summary>
    RecentlyUpdated = 1,

    /// <summary>Сначала недавно созданные.</summary>
    RecentlyCreated = 2,

    /// <summary>По лицевой стороне, по алфавиту.</summary>
    Alphabetical = 3,

    /// <summary>По сроку повторения: ближайшие сверху, новые карточки без срока — в конце.</summary>
    DueDate = 4,
}
