using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты сверки введённого ответа (new_addons.md §5.3, Phase 12.8).
/// </summary>
public sealed class AnswerMatchingTests
{
    // --- нормализация -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Предел", "предел")]
    [InlineData("  предел  ", "предел")]
    [InlineData("Ёмкость", "емкость")]
    [InlineData("предел,  функции.", "предел функции")]
    [InlineData("предел\tфункции", "предел функции")]
    [InlineData("«предел»", "предел")]
    [InlineData("f(x) = 2x", "f x 2x")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("...", "")]
    public void Normalization_makes_answers_comparable(string input, string expected)
    {
        Assert.Equal(expected, AnswerMatching.Normalize(input));
    }

    [Fact]
    public void Null_normalizes_to_empty()
    {
        Assert.Equal(string.Empty, AnswerMatching.Normalize(null));
    }

    [Fact]
    public void A_non_breaking_space_is_a_separator_like_any_other()
    {
        Assert.Equal("предел функции", AnswerMatching.Normalize("предел функции"));
    }

    // --- вердикты ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("предел", "Предел")]
    [InlineData("Емкость", "Ёмкость")]
    [InlineData("предел функции.", "предел, функции")]
    public void An_exact_match_after_normalization_is_correct(string input, string reference)
    {
        var result = AnswerMatching.Match(input, reference);

        Assert.Equal(AnswerVerdict.Correct, result.Verdict);
        Assert.Equal(1, result.Similarity, 6);
    }

    [Fact]
    public void A_single_letter_answer_short_circuits_instead_of_scoring_zero()
    {
        // Токенизатор выбрасывает однобуквенные токены как шум, поэтому без короткого замыкания
        // верный ответ «x» получил бы схожесть 0 и был бы засчитан как неверный.
        var result = AnswerMatching.Match("x", "X");

        Assert.Equal(AnswerVerdict.Correct, result.Verdict);
    }

    [Fact]
    public void A_near_miss_is_close_and_shows_the_similarity()
    {
        var result = AnswerMatching.Match(
            "предел функции в точке",
            "предел функции в точке a");

        Assert.Equal(AnswerVerdict.Close, result.Verdict);
        Assert.InRange(result.Similarity, 0.8, 1.0);
    }

    [Fact]
    public void A_different_answer_is_wrong()
    {
        var result = AnswerMatching.Match("производная", "первообразная функции");

        Assert.Equal(AnswerVerdict.Wrong, result.Verdict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_answer_is_always_wrong(string? input)
    {
        Assert.Equal(AnswerVerdict.Wrong, AnswerMatching.Match(input, "предел").Verdict);
    }

    [Fact]
    public void An_empty_reference_is_wrong_rather_than_a_free_pass()
    {
        Assert.Equal(AnswerVerdict.Wrong, AnswerMatching.Match("что угодно", "").Verdict);
    }

    [Fact]
    public void The_threshold_moves_the_line_between_close_and_wrong()
    {
        const string input = "предел последовательности";
        const string reference = "предел последовательности точек схождения";

        Assert.Equal(AnswerVerdict.Close, AnswerMatching.Match(input, reference, 0.5).Verdict);
        Assert.Equal(AnswerVerdict.Wrong, AnswerMatching.Match(input, reference, 0.99).Verdict);
    }

    [Fact]
    public void The_result_carries_both_normalized_strings_for_the_ui()
    {
        var result = AnswerMatching.Match("Ёмкость!", "емкость");

        Assert.Equal("емкость", result.NormalizedInput);
        Assert.Equal("емкость", result.NormalizedReference);
    }

    // --- доступность режима -------------------------------------------------------------------------

    [Theory]
    [InlineData(1, true)]
    [InlineData(119, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    [InlineData(500, false)]
    public void Typing_mode_is_offered_only_for_short_references(int length, bool expected)
    {
        Assert.Equal(expected, AnswerMatching.IsTypingModeAvailable(new string('а', length)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Typing_mode_is_not_offered_without_a_reference(string? reference)
    {
        Assert.False(AnswerMatching.IsTypingModeAvailable(reference));
    }

    [Fact]
    public void Surrounding_whitespace_does_not_count_towards_the_limit()
    {
        Assert.True(AnswerMatching.IsTypingModeAvailable("   " + new string('а', 120) + "   "));
    }

    // --- перевод в оценку -----------------------------------------------------------------------------

    [Theory]
    [InlineData(AnswerVerdict.Correct, ReviewGrade.Good)]
    [InlineData(AnswerVerdict.Close, ReviewGrade.Hard)]
    [InlineData(AnswerVerdict.Wrong, ReviewGrade.Again)]
    public void A_verdict_maps_to_a_grade(AnswerVerdict verdict, ReviewGrade expected)
    {
        // «Почти верно» — это «вспомнил с трудом», а не провал: опечатка не должна стоить
        // пользователю всего накопленного интервала.
        Assert.Equal(expected, AnswerMatching.ToGrade(verdict));
    }
}
