namespace StudComp.Core.Domain;

/// <summary>
/// Колода карточек (new_addons.md §3.1) — набор под коллоквиум, модуль или экзамен.
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
/// <remarks>
/// Один тип, два поведения: пустой <see cref="QueryExpression"/> — обычная колода, карточки лежат в
/// ней по <see cref="Card.DeckId"/>; заданный — «умная подборка», сохранённый поисковый запрос,
/// содержимое которой вычисляется на лету. Второй сущности ради этого заводить незачем
/// (new_addons.md §13.8).
/// </remarks>
public sealed class CardDeck
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Предмет колоды; <see langword="null"/> — колода вне предмета.</summary>
    public Guid? SubjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Цвет колоды в формате <c>#RRGGBB</c>; пусто — наследует цвет предмета.</summary>
    public string? ColorHex { get; set; }

    /// <summary>Позиция в списке колод предмета.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Сохранённый поисковый запрос умной подборки в синтаксисе строки поиска (new_addons.md §4.4).
    /// Пусто — обычная колода.
    /// </summary>
    public string? QueryExpression { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
