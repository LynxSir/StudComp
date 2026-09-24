namespace StudComp.Core.Domain;

/// <summary>
/// Шаблон отчёта: имя + вариант ГОСТ + сериализованный профиль стиля
/// (ARCHITECTURE §7.1 <c>REPORT_TEMPLATE</c>, §10.4).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class ReportTemplate
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Обозначение варианта ГОСТ, например «7.32-2017».</summary>
    public string GostVariant { get; set; } = string.Empty;

    /// <summary>
    /// <c>GostStyleProfile</c> (ARCHITECTURE §10.4), сериализованный в JSON. Хранится как строка,
    /// разбирается в модуле ReportForge — <c>Data</c> в его структуру не заглядывает.
    /// </summary>
    public string StyleProfileJson { get; set; } = string.Empty;
}
