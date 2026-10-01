using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Ограничители формул из языковых моделей. Никакой ввод не должен приводить к исключению.
/// </summary>
public sealed class MathDelimitersTests
{
    [Theory]
    [InlineData(@"\(s\)", "$s$")]
    [InlineData(@"где \(s\) — скольжение", "где $s$ — скольжение")]
    [InlineData(@"\(a\) и \(b\)", "$a$ и $b$")]
    [InlineData(@"\[E=mc^2\]", "$$E=mc^2$$")]
    [InlineData(@"текст\[x\]текст", "текст$$x$$текст")]
    public void Tex_delimiters_become_dollars(string source, string expected) =>
        Assert.Equal(expected, MathDelimiters.Normalize(source));

    [Fact]
    public void An_inner_backslash_command_survives() =>
        Assert.Equal(@"$\frac{a}{b}$", MathDelimiters.Normalize(@"\(\frac{a}{b}\)"));

    [Theory]
    [InlineData(@"\(незакрытая")]
    [InlineData(@"\[тоже незакрытая")]
    [InlineData(@"закрывающая без открывающей\)")]
    public void An_unterminated_sequence_stays_text(string source) =>
        Assert.Equal(source, MathDelimiters.Normalize(source));

    [Fact]
    public void Code_spans_and_fences_are_left_alone()
    {
        var source = string.Join("\n", [@"`\(x\)`", "```", @"\(y\)", "```", @"\(z\)"]);

        var result = MathDelimiters.Normalize(source);

        Assert.Contains(@"`\(x\)`", result);
        Assert.Contains(@"\(y\)", result);
        Assert.EndsWith("$z$", result);
    }

    [Fact]
    public void An_escaped_backslash_is_not_a_delimiter() =>
        Assert.Equal(@"\(не формула)", MathDelimiters.Normalize(@"\(не формула)"));

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("обычный текст", "обычный текст")]
    [InlineData("$уже доллары$", "$уже доллары$")]
    [InlineData(@"\", @"\")]
    [InlineData(@"\(", @"\(")]
    public void Anything_else_passes_through(string? source, string expected) =>
        Assert.Equal(expected, MathDelimiters.Normalize(source));

    [Fact]
    public void The_owners_note_converts_end_to_end()
    {
        var source = string.Join("\n", [@"- \(s\) — скольжение;", @"- \(p\) — число пар полюсов;"]);

        var result = MathDelimiters.Normalize(source);

        Assert.Equal(string.Join("\n", ["- $s$ — скольжение;", "- $p$ — число пар полюсов;"]), result);
    }
}
