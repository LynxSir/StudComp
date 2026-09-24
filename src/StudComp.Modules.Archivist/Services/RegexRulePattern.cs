using System.Text.RegularExpressions;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Проверка корректности regex-правила — одна на все слои (движок, сервис, форма редактора).
/// Настройки те же, что применяет <c>SortingRuleEngine</c>.
/// </summary>
public static class RegexRulePattern
{
    /// <summary>Компилируется ли <paramref name="pattern"/> как регулярное выражение.</summary>
    public static bool IsValid(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            _ = new Regex(pattern.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
