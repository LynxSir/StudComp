using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты <see cref="TokenSimilarity"/> (new_addons.md §4, Phase 12.2) — общий инструмент,
/// вынесенный из <c>DeadlineMatching</c> (Organizer, Phase 10) ради второго потребителя
/// (<c>IUnsortedSuggestionService</c>, Archivist).
/// </summary>
public sealed class TokenSimilarityTests
{
    [Theory]
    [InlineData("ЛР4", new[] { "лр", "4" })]
    [InlineData("ЛР04", new[] { "лр", "4" })]           // ведущие нули — тот же номер работы
    [InlineData("Лекция 3 про пределы", new[] { "лекция", "3", "про", "пределы" })]
    [InlineData("отчёт", new[] { "отчет" })]
    [InlineData("a-b-c", new string[0])]                 // одиночные буквы — шум
    public void Tokenize_splits_letters_and_digits(string value, string[] expected)
    {
        Assert.Equal(expected.OrderBy(t => t), TokenSimilarity.Tokenize(value).OrderBy(t => t));
    }

    [Fact]
    public void Dice_of_identical_sets_is_one()
    {
        var set = TokenSimilarity.Tokenize("матан лекция");

        Assert.Equal(1.0, TokenSimilarity.Dice(set, set), 6);
    }

    [Fact]
    public void Dice_of_disjoint_sets_is_zero()
    {
        var left = TokenSimilarity.Tokenize("матан");
        var right = TokenSimilarity.Tokenize("физика");

        Assert.Equal(0.0, TokenSimilarity.Dice(left, right));
    }

    [Fact]
    public void Dice_of_two_empty_sets_is_zero_not_a_division_error()
    {
        // Метод публичный — вызывающая сторона не обязана заранее отсекать пустые наборы, как это
        // делает DeadlineMatching.Score до своего внутреннего вызова.
        Assert.Equal(0.0, TokenSimilarity.Dice([], []));
    }

    [Fact]
    public void Dice_accounts_for_set_size_asymmetry()
    {
        var small = TokenSimilarity.Tokenize("матан");
        var large = TokenSimilarity.Tokenize("матан лекция конспект семинар");

        // 1 общий токен из 1 и 4 → 2*1/(1+4) = 0.4.
        Assert.Equal(0.4, TokenSimilarity.Dice(small, large), 6);
    }

    [Fact]
    public void Dice_is_symmetric()
    {
        var left = TokenSimilarity.Tokenize("ЛР4 матан");
        var right = TokenSimilarity.Tokenize("матан ЛР5");

        Assert.Equal(TokenSimilarity.Dice(left, right), TokenSimilarity.Dice(right, left), 6);
    }
}
