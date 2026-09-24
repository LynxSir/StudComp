namespace StudComp.Resources;

/// <summary>
/// Русская плюрализация числительных («1 пара» / «2 пары» / «5 пар») — обобщение инлайн-логики,
/// которая раньше жила только в <c>DashboardPageViewModel.CardsDueText</c> (Phase 12.7); Phase 13.5
/// переиспользует её и для сводки дня на Дашборде.
/// </summary>
public static class RussianPlural
{
    /// <summary>Возвращает форму слова, соответствующую <paramref name="count"/> (11–14 — всегда «many»).</summary>
    public static string Of(int count, string one, string few, string many)
    {
        var n = Math.Abs(count);
        if (n % 10 == 1 && n % 100 != 11)
        {
            return one;
        }

        if (n % 10 is >= 2 and <= 4 && n % 100 is < 11 or > 14)
        {
            return few;
        }

        return many;
    }
}
