using StudComp.Core.Domain;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Похожесть «имя файла ↔ название дедлайна» (ARCHITECTURE §9.3): та самая эвристика, из-за которой
/// после сортировки «ЛР4_Матан.docx» всплывает предложение «Похоже, это ЛР4 по Матану — привязать
/// к дедлайну 10.09?».
/// </summary>
/// <remarks>
/// Чистая функция: ни диска, ни БД, ни времени «сейчас» — всё приходит параметрами, поэтому она
/// целиком проверяется табличными тестами (как <c>WeekParityCalculator</c>). Живёт в модуле, а не в
/// <c>Core</c>: второго потребителя у неё нет (ADR §16.56).
/// </remarks>
internal static class DeadlineMatching
{
    // Доли итогового счёта. Текст — основа, номер работы («ЛР4» против «ЛР5») различает почти всё,
    // близость к сроку лишь подталкивает выбор между двумя одинаково названными дедлайнами.
    private const double TextWeight = 0.6;
    private const double NumberWeight = 0.25;
    private const double DateWeight = 0.15;

    /// <summary>
    /// Счёт похожести в диапазоне [0, 1].
    /// </summary>
    /// <param name="fileName">Имя файла с расширением — расширение отбрасывается внутри.</param>
    /// <param name="deadlineTitle">Название дедлайна.</param>
    /// <param name="dueDate">Срок дедлайна.</param>
    /// <param name="sortedAt">Когда файл был разложен.</param>
    /// <param name="windowDays">Ширина окна вокруг срока, за пределами которого близость не засчитывается.</param>
    public static double Score(
        string fileName,
        string deadlineTitle,
        DateTimeOffset dueDate,
        DateTimeOffset sortedAt,
        int windowDays)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(deadlineTitle))
        {
            return 0;
        }

        var fileTokens = Tokenize(Path.GetFileNameWithoutExtension(fileName));
        var titleTokens = Tokenize(deadlineTitle);

        if (fileTokens.Count == 0 || titleTokens.Count == 0)
        {
            return 0;
        }

        var number = NumberAffinity(fileTokens, titleTokens);
        if (number is null)
        {
            // Номера есть с обеих сторон и они разные: «ЛР4» против «ЛР5» — это разные работы,
            // сколько бы ни совпадало остальное. Предлагать такую привязку хуже, чем молчать.
            return 0;
        }

        var text = TokenSimilarity.Dice(fileTokens, titleTokens);
        var date = DateAffinity(dueDate, sortedAt, windowDays);

        var score = (text * TextWeight) + (number.Value * NumberWeight) + (date * DateWeight);
        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Тонкая обёртка над <see cref="TokenSimilarity.Tokenize"/> — оставлена, чтобы не трогать
    /// существующие тесты (<c>DeadlineMatchingTests.Tokenizer_splits_letters_and_digits</c> вызывает
    /// именно этот метод напрямую). Сам алгоритм — общий, второй потребитель — Archivist (§4).
    /// </summary>
    internal static HashSet<string> Tokenize(string value) => TokenSimilarity.Tokenize(value);

    /// <summary>
    /// Номер работы: совпал — уверенный плюс, номеров нет ни у кого (или только у одной стороны) —
    /// нейтрально. <see langword="null"/> означает «вето»: номера есть с обеих сторон и они разные.
    /// </summary>
    private static double? NumberAffinity(HashSet<string> fileTokens, HashSet<string> titleTokens)
    {
        var fileNumbers = fileTokens.Where(IsNumber).ToList();
        var titleNumbers = titleTokens.Where(IsNumber).ToList();

        if (fileNumbers.Count == 0 || titleNumbers.Count == 0)
        {
            return 0.5;
        }

        return fileNumbers.Any(titleNumbers.Contains) ? 1.0 : null;
    }

    /// <summary>
    /// Близость момента сортировки к сроку: в день сдачи — единица, на краю окна — ноль. Знак
    /// разницы не важен, работу сдают и заранее, и в последний момент.
    /// </summary>
    private static double DateAffinity(DateTimeOffset dueDate, DateTimeOffset sortedAt, int windowDays)
    {
        if (windowDays <= 0)
        {
            return 0;
        }

        var distanceDays = Math.Abs((dueDate - sortedAt).TotalDays);
        return distanceDays >= windowDays ? 0 : 1.0 - (distanceDays / windowDays);
    }

    private static bool IsNumber(string token) => token.Length > 0 && char.IsAsciiDigit(token[0]);
}
