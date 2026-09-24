namespace StudComp.Resources;

/// <summary>
/// Единый формат отображения даты в интерфейсе (new_addons.md §8, Тест.txt №24) — не зависит от
/// локали ОС. Применять всегда с <see cref="System.Globalization.CultureInfo.InvariantCulture"/>:
/// у неё разделитель дат — «/», поэтому «/» в формате печатается буквально, а не подменяется
/// культурным разделителем.
/// </summary>
public static class AppDateFormat
{
    public const string ShortDate = "dd/MM/yy";
}
