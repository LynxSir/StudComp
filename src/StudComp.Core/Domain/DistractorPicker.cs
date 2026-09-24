namespace StudComp.Core.Domain;

/// <summary>Откуда взят кандидат в «отвлекающие» варианты — чем ближе к карточке, тем правдоподобнее.</summary>
public enum DistractorScope
{
    Deck = 0,
    Subject = 1,
    Global = 2,
}

/// <summary>Кандидат в неверные варианты: оборот чужой карточки.</summary>
public sealed record DistractorCandidate(Guid CardId, string Text, DistractorScope Scope);

/// <summary>
/// Готовый набор вариантов для теста.
/// </summary>
/// <param name="Options">Четыре варианта в порядке показа; пусто при вырождении.</param>
/// <param name="CorrectIndex">Позиция верного варианта; <c>-1</c> при вырождении.</param>
/// <param name="IsDegraded">
/// Годных кандидатов не хватило — карточку надо молча предъявить как самооценочную (new_addons.md §5.3).
/// </param>
public sealed record DistractorSet(IReadOnlyList<string> Options, int CorrectIndex, bool IsDegraded)
{
    /// <summary>Вырожденный набор — режим теста для этой карточки недоступен.</summary>
    public static DistractorSet Degraded { get; } = new([], -1, true);
}

/// <summary>
/// Подбор «отвлекающих» вариантов для режима «выбор варианта» (new_addons.md §5.3). Чистая функция:
/// от зерна сессии зависит и отбор, и позиция верного ответа, поэтому вопрос воспроизводится
/// в точности при повторе сессии.
/// </summary>
public static class DistractorPicker
{
    /// <summary>
    /// Кандидат, похожий на верный ответ сильнее этого порога, отбрасывается: иначе получится вопрос
    /// без однозначного ответа, а это хуже отсутствия вопроса.
    /// </summary>
    public const double RejectSimilarity = 0.5;

    /// <summary>Сколько неверных вариантов нужно для честного теста.</summary>
    public const int RequiredDistractors = 3;

    /// <summary>Собрать набор вариантов.</summary>
    public static DistractorSet Pick(
        string correctAnswer,
        IReadOnlyList<DistractorCandidate> candidates,
        int seed,
        double rejectSimilarity = RejectSimilarity,
        int requiredDistractors = RequiredDistractors)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var normalizedCorrect = AnswerMatching.Normalize(correctAnswer);
        if (normalizedCorrect.Length == 0 || requiredDistractors <= 0)
        {
            return DistractorSet.Degraded;
        }

        var correctTokens = TokenSimilarity.Tokenize(normalizedCorrect);
        var rng = new StudySessionPlanner.Rng(seed);

        var picked = new List<string>(requiredDistractors);
        var pickedTokens = new List<HashSet<string>>(requiredDistractors);
        var seenTexts = new HashSet<string>(StringComparer.Ordinal) { normalizedCorrect };

        // Сначала соседи по колоде, потом по предмету, потом кто угодно: чем ближе материал,
        // тем правдоподобнее неверный вариант.
        foreach (var scope in (ReadOnlySpan<DistractorScope>)[DistractorScope.Deck, DistractorScope.Subject, DistractorScope.Global])
        {
            var tier = candidates.Where(c => c is not null && c.Scope == scope).ToList();
            Shuffle(tier, ref rng);

            foreach (var candidate in tier)
            {
                if (picked.Count >= requiredDistractors)
                {
                    break;
                }

                var normalized = AnswerMatching.Normalize(candidate.Text);
                if (normalized.Length == 0 || !seenTexts.Add(normalized))
                {
                    continue;
                }

                var tokens = TokenSimilarity.Tokenize(normalized);
                if (TokenSimilarity.Dice(tokens, correctTokens) >= rejectSimilarity)
                {
                    continue;
                }

                // Два дистрактора, одинаковых по смыслу, обесценивают вопрос ровно так же,
                // как дистрактор, похожий на верный ответ.
                if (pickedTokens.Any(existing => TokenSimilarity.Dice(tokens, existing) >= rejectSimilarity))
                {
                    continue;
                }

                picked.Add(candidate.Text.Trim());
                pickedTokens.Add(tokens);
            }

            if (picked.Count >= requiredDistractors)
            {
                break;
            }
        }

        if (picked.Count < requiredDistractors)
        {
            return DistractorSet.Degraded;
        }

        var correctIndex = rng.Next(requiredDistractors + 1);
        var options = new List<string>(requiredDistractors + 1);
        options.AddRange(picked);
        options.Insert(correctIndex, correctAnswer.Trim());

        return new DistractorSet(options, correctIndex, false);
    }

    private static void Shuffle(List<DistractorCandidate> items, ref StudySessionPlanner.Rng rng)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
