using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты подбора «отвлекающих» вариантов (new_addons.md §5.3, Phase 12.8). Главное здесь —
/// не эстетика вопроса, а невозможность собрать тест с двумя правильными ответами.
/// </summary>
public sealed class DistractorPickerTests
{
    private const string Correct = "предел последовательности";

    /// <summary>Заведомо непохожие друг на друга ответы — из них собираются честные наборы.</summary>
    private static readonly string[] Unrelated =
    [
        "производная сложной функции",
        "интеграл по частям",
        "матрица перехода базиса",
        "теорема Коши о среднем",
        "ряд Тейлора в нуле",
        "собственные числа оператора",
        "градиент скалярного поля",
        "векторное произведение",
        "нормальное распределение",
        "критерий Сильвестра",
    ];

    private static DistractorCandidate Candidate(string text, DistractorScope scope = DistractorScope.Deck) =>
        new(Guid.NewGuid(), text, scope);

    private static List<DistractorCandidate> Pool(
        int count,
        DistractorScope scope = DistractorScope.Deck,
        int offset = 0) =>
        [.. Unrelated.Skip(offset).Take(count).Select(t => Candidate(t, scope))];

    [Fact]
    public void Four_options_are_produced_with_the_correct_one_among_them()
    {
        var set = DistractorPicker.Pick(Correct, Pool(5), 1);

        Assert.False(set.IsDegraded);
        Assert.Equal(4, set.Options.Count);
        Assert.Equal(Correct, set.Options[set.CorrectIndex]);
        Assert.Equal(4, set.Options.Distinct().Count());
    }

    [Fact]
    public void A_candidate_equal_to_the_correct_answer_is_rejected()
    {
        List<DistractorCandidate> pool =
        [
            Candidate("Предел последовательности."),   // тот же ответ другой пунктуацией
            .. Pool(3),
        ];

        var set = DistractorPicker.Pick(Correct, pool, 1);

        var sameAsCorrect = set.Options
            .Count(o => AnswerMatching.Normalize(o) == AnswerMatching.Normalize(Correct));

        Assert.Equal(1, sameAsCorrect);
    }

    [Fact]
    public void A_candidate_too_similar_to_the_correct_answer_is_rejected()
    {
        // Иначе получится вопрос без однозначного ответа — хуже, чем отсутствие вопроса.
        List<DistractorCandidate> pool =
        [
            Candidate("предел последовательности чисел"),
            .. Pool(3),
        ];

        var set = DistractorPicker.Pick(Correct, pool, 1);

        Assert.DoesNotContain("предел последовательности чисел", set.Options);
    }

    [Fact]
    public void Two_distractors_that_mean_the_same_are_not_taken_together()
    {
        List<DistractorCandidate> pool =
        [
            Candidate("производная сложной функции"),
            Candidate("производная сложной функции одной"),
            .. Pool(3, offset: 1),
        ];

        var set = DistractorPicker.Pick(Correct, pool, 3);

        var taken = set.Options.Count(o => o.StartsWith("производная", StringComparison.Ordinal));

        Assert.True(taken <= 1, "в вопрос попали два одинаковых по смыслу неверных варианта");
    }

    [Fact]
    public void Nearer_candidates_win()
    {
        List<DistractorCandidate> pool =
        [
            .. Pool(3, DistractorScope.Global),
            .. Pool(3, DistractorScope.Deck, offset: 3),
        ];
        var deckTexts = Unrelated.Skip(3).Take(3).ToHashSet();

        var set = DistractorPicker.Pick(Correct, pool, 5);

        Assert.All(set.Options.Where(o => o != Correct), o => Assert.Contains(o, deckTexts));
    }

    [Fact]
    public void The_subject_tier_is_used_before_the_global_one()
    {
        List<DistractorCandidate> pool =
        [
            .. Pool(3, DistractorScope.Global),
            .. Pool(3, DistractorScope.Subject, offset: 3),
        ];
        var subjectTexts = Unrelated.Skip(3).Take(3).ToHashSet();

        var set = DistractorPicker.Pick(Correct, pool, 5);

        Assert.All(set.Options.Where(o => o != Correct), o => Assert.Contains(o, subjectTexts));
    }

    [Fact]
    public void Tiers_are_topped_up_from_the_next_one_when_the_nearest_runs_dry()
    {
        List<DistractorCandidate> pool =
        [
            .. Pool(1, DistractorScope.Deck),
            .. Pool(4, DistractorScope.Global, offset: 1),
        ];

        var set = DistractorPicker.Pick(Correct, pool, 5);

        Assert.False(set.IsDegraded);
        Assert.Contains(Unrelated[0], set.Options);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Too_few_usable_candidates_degrade_the_mode(int available)
    {
        var set = DistractorPicker.Pick(Correct, Pool(available), 1);

        Assert.True(set.IsDegraded);
        Assert.Empty(set.Options);
        Assert.Equal(-1, set.CorrectIndex);
    }

    [Fact]
    public void An_empty_correct_answer_degrades_the_mode()
    {
        Assert.True(DistractorPicker.Pick("   ", Pool(5), 1).IsDegraded);
    }

    [Fact]
    public void Empty_candidates_do_not_count_towards_the_quota()
    {
        List<DistractorCandidate> pool =
        [
            Candidate("   "),
            Candidate("..."),
            .. Pool(2),
        ];

        Assert.True(DistractorPicker.Pick(Correct, pool, 1).IsDegraded);
    }

    [Fact]
    public void The_same_seed_gives_the_same_question()
    {
        var pool = Pool(10);

        var first = DistractorPicker.Pick(Correct, pool, 777);
        var second = DistractorPicker.Pick(Correct, pool, 777);

        Assert.Equal(first.Options, second.Options);
        Assert.Equal(first.CorrectIndex, second.CorrectIndex);
    }

    [Fact]
    public void The_correct_answer_does_not_always_sit_in_the_same_slot()
    {
        var pool = Pool(10);
        var positions = new HashSet<int>();

        for (var seed = 1; seed <= 100; seed++)
        {
            positions.Add(DistractorPicker.Pick(Correct, pool, seed).CorrectIndex);
        }

        Assert.Equal([0, 1, 2, 3], positions.OrderBy(p => p));
    }

    [Fact]
    public void A_custom_quota_is_honoured()
    {
        var set = DistractorPicker.Pick(Correct, Pool(2), 1, requiredDistractors: 2);

        Assert.False(set.IsDegraded);
        Assert.Equal(3, set.Options.Count);
    }
}
