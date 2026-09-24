using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace StudComp.Data.Configurations;

/// <summary>
/// Общие <see cref="ValueConverter"/> для EF-конфигураций.
/// </summary>
internal static class DataConverters
{
    /// <summary>
    /// <see cref="decimal"/> ↔ <see cref="string"/> через инвариантную культуру. У SQLite нет типа
    /// <c>decimal</c>; конверсия через текущую культуру ломала бы разделитель дробной части и
    /// теряла точность (ARCHITECTURE §7.2). Сравнения по этим полям в БД не нужны — прогноз
    /// оценок считается в памяти (ARCHITECTURE §9.4).
    /// </summary>
    public static readonly ValueConverter<decimal, string> DecimalToInvariantString = new(
        value => value.ToString(CultureInfo.InvariantCulture),
        value => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture));

    /// <summary>
    /// То же для <see cref="Nullable{Decimal}"/>: <see langword="null"/> ↔ <see langword="null"/>.
    /// Нужен произвольным границам шкалы оценивания предмета (ARCHITECTURE §9.4) — они заданы не всегда.
    /// </summary>
    public static readonly ValueConverter<decimal?, string?> NullableDecimalToInvariantString = new(
        value => value == null ? null : value.Value.ToString(CultureInfo.InvariantCulture),
        value => string.IsNullOrEmpty(value) ? null : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture));

    /// <summary>
    /// <see cref="DateTimeOffset"/> ↔ <see cref="DateTime"/> в UTC. SQLite не поддерживает
    /// <c>DateTimeOffset</c> в <c>WHERE</c>/<c>ORDER BY</c>, поэтому в домене тип остаётся
    /// <c>DateTimeOffset</c> (удобный API), а на диск пишется мгновение в UTC — этого хватает для
    /// «просрочен ли дедлайн» и сортировок по времени (ARCHITECTURE §9.2–9.3). Исходное смещение
    /// не сохраняется — оно нам нигде не нужно.
    /// </summary>
    public static readonly ValueConverter<DateTimeOffset, DateTime> DateTimeOffsetToUtc = new(
        value => value.UtcDateTime,
        value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero));

    /// <summary>
    /// То же для <see cref="Nullable{DateTimeOffset}"/>. Нужен карточкам: срок повторения, время
    /// последнего показа и метка мягкого удаления заданы не всегда (new_addons.md §3.1–3.2).
    /// </summary>
    public static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableDateTimeOffsetToUtc = new(
        value => value == null ? null : value.Value.UtcDateTime,
        value => value == null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc), TimeSpan.Zero));
}
