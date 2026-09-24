namespace StudComp.Core.Domain;

/// <summary>
/// Метка карточки (new_addons.md §3.1). Метки <b>глобальные</b>, не привязаны к предмету:
/// <c>#формулы</c> осмысленно и в матане, и в физике.
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class CardTag
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Нормализованное имя: нижний регистр, без ведущей решётки. По нему ищут и сравнивают.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Имя как его написал пользователь — показывается в чипах.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string? ColorHex { get; set; }

    /// <summary>
    /// Денормализованный счётчик использований — им сортируются облако меток и автодополнение.
    /// Пересчитывается сервисом меток, а не запросом на каждый показ.
    /// </summary>
    public int UsageCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Связка «карточка ↔ метка» (new_addons.md §3.1) — чистая join-таблица с составным первичным
/// ключом. Обе стороны каскадные: без карточки или без метки связка бессмысленна.
/// </summary>
public sealed class CardTagLink
{
    public Guid CardId { get; set; }

    public Guid TagId { get; set; }
}
