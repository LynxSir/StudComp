using System.Globalization;
using System.Text;

namespace StudComp.Core.Domain;

/// <summary>
/// Похожесть по токенам: разбор строки на сравнимые куски и коэффициент Дайса. Общий инструмент для
/// «файл ↔ дедлайн» (<c>DeadlineMatching</c>, Organizer, Phase 10) и «файл ↔ предмет»
/// (<c>IUnsortedSuggestionService</c>, Archivist, Phase 12.2) — второй потребитель и стал поводом
/// вынести эти два метода сюда из модуля (new_addons.md §4).
/// </summary>
public static class TokenSimilarity
{
    /// <summary>
    /// Разбор строки на сравнимые куски: нижний регистр, буквы и цифры расходятся по разным токенам
    /// («ЛР4» → «лр» + «4»), ведущие нули у чисел обрезаются («04» и «4» — один номер), однобуквенные
    /// токены выбрасываются как шум («в», «и», «№»). «Ё» приводится к «е» — в именах файлов её пишут
    /// как придётся.
    /// </summary>
    public static HashSet<string> Tokenize(string value)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var buffer = new StringBuilder();
        var bufferIsDigits = false;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var token = buffer.ToString();
            if (bufferIsDigits)
            {
                var trimmed = token.TrimStart('0');
                tokens.Add(trimmed.Length == 0 ? "0" : trimmed);
            }
            else if (token.Length > 1)
            {
                tokens.Add(token);
            }

            buffer.Clear();
        }

        foreach (var ch in value.ToLower(CultureInfo.InvariantCulture))
        {
            var isDigit = char.IsAsciiDigit(ch);
            var isLetter = char.IsLetter(ch);

            if (!isDigit && !isLetter)
            {
                Flush();
                continue;
            }

            if (buffer.Length > 0 && isDigit != bufferIsDigits)
            {
                Flush();
            }

            bufferIsDigits = isDigit;
            buffer.Append(ch == 'ё' ? 'е' : ch);
        }

        Flush();
        return tokens;
    }

    /// <summary>
    /// Коэффициент Дайса: доля общих токенов, устойчивая к разной длине множеств. Пустые множества
    /// с обеих сторон дают 0, а не деление на ноль — это публичный метод, вызывающая сторона не обязана
    /// заранее отсеивать пустые наборы (в отличие от специфичных для дедлайнов вызовов в модуле).
    /// </summary>
    public static double Dice(ICollection<string> left, ICollection<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 0;
        }

        var shared = left.Count <= right.Count
            ? left.Count(right.Contains)
            : right.Count(left.Contains);

        return 2.0 * shared / (left.Count + right.Count);
    }
}
