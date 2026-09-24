using System.Globalization;
using System.Text;

namespace StudComp.Core.Domain;

/// <summary>Насколько введённый ответ похож на эталон (new_addons.md §5.3).</summary>
public enum AnswerVerdict
{
    /// <summary>Совпал после нормализации.</summary>
    Correct = 0,

    /// <summary>Почти верно: показываем эталон и даём право сказать «засчитать».</summary>
    Close = 1,

    /// <summary>Не то.</summary>
    Wrong = 2,
}

/// <summary>Итог сверки введённого ответа с эталоном.</summary>
public sealed record AnswerMatchResult(
    AnswerVerdict Verdict,
    double Similarity,
    string NormalizedInput,
    string NormalizedReference);

/// <summary>
/// Сверка введённого ответа с эталоном (new_addons.md §5.3): нормализация, точное совпадение,
/// иначе нечёткое сравнение поверх уже существующего <see cref="TokenSimilarity"/>. Чистая функция
/// на голом BCL.
/// </summary>
public static class AnswerMatching
{
    /// <summary>
    /// Длиннее этого эталона режим ввода просто не предлагается: набирать абзац определения руками —
    /// наказание, а не упражнение.
    /// </summary>
    public const int MaxReferenceLength = 120;

    /// <summary>Порог «почти верно» по умолчанию; правится в настройках раздела.</summary>
    public const double DefaultCloseThreshold = 0.8;

    /// <summary>
    /// Привести ответ к сравнимому виду: обрезать края, сложить регистр, «ё» к «е», выбросить
    /// пунктуацию и схлопнуть пробелы. «Мощность множества.» и «мощность  множества» — один ответ.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var raw in value.ToLower(CultureInfo.InvariantCulture))
        {
            var ch = raw == 'ё' ? 'е' : raw;

            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(ch);
                continue;
            }

            // Пунктуация и пробелы одинаково служат разделителями: «а,б» и «а б» сравниваются одинаково.
            pendingSpace = true;
        }

        return builder.ToString();
    }

    /// <summary>Уместен ли режим «ввод ответа» для этого эталона.</summary>
    /// <remarks>
    /// Звать надо на <b>раскрытом</b> тексте (<see cref="ClozeText.ToPlainText"/>), иначе фигурные
    /// скобки пропусков съедят лимит длины.
    /// </remarks>
    public static bool IsTypingModeAvailable(string? reference)
    {
        var trimmed = reference?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= MaxReferenceLength;
    }

    /// <summary>Сверить введённое с эталоном.</summary>
    public static AnswerMatchResult Match(
        string? input,
        string? reference,
        double closeThreshold = DefaultCloseThreshold)
    {
        var normalizedInput = Normalize(input);
        var normalizedReference = Normalize(reference);

        if (normalizedInput.Length == 0 || normalizedReference.Length == 0)
        {
            return new AnswerMatchResult(AnswerVerdict.Wrong, 0, normalizedInput, normalizedReference);
        }

        // Короткое замыкание обязательно: TokenSimilarity.Tokenize выбрасывает однобуквенные токены
        // как шум, и без этой проверки верный ответ «x» получил бы схожесть 0.
        if (string.Equals(normalizedInput, normalizedReference, StringComparison.Ordinal))
        {
            return new AnswerMatchResult(AnswerVerdict.Correct, 1, normalizedInput, normalizedReference);
        }

        var similarity = TokenSimilarity.Dice(
            TokenSimilarity.Tokenize(normalizedInput),
            TokenSimilarity.Tokenize(normalizedReference));

        var verdict = similarity >= closeThreshold ? AnswerVerdict.Close : AnswerVerdict.Wrong;
        return new AnswerMatchResult(verdict, similarity, normalizedInput, normalizedReference);
    }

    /// <summary>
    /// Во что превращается вердикт автопроверки: «почти верно» — это «вспомнил с трудом», а не провал,
    /// иначе опечатка стоила бы пользователю всего накопленного интервала.
    /// </summary>
    public static ReviewGrade ToGrade(AnswerVerdict verdict) => verdict switch
    {
        AnswerVerdict.Correct => ReviewGrade.Good,
        AnswerVerdict.Close => ReviewGrade.Hard,
        _ => ReviewGrade.Again,
    };
}
