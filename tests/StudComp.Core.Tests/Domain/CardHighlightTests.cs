using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Разрезание текста карточки на подсвеченные куски (new_addons.md §4.5). Главный инвариант тот же,
/// что у разбора запроса: любая пользовательская строка обрабатывается без исключения.
/// </summary>
public sealed class CardHighlightTests
{
    private static string Mark(string text) => CardHighlight.MarkerStart + text + CardHighlight.MarkerEnd;

    [Fact]
    public void Snippet_markers_become_highlighted_segments()
    {
        var segments = CardHighlight.FromSnippet($"Формула {Mark("Ньютона")} и её следствие");

        Assert.Equal(3, segments.Count);
        Assert.False(segments[0].IsMatch);
        Assert.True(segments[1].IsMatch);
        Assert.Equal("Ньютона", segments[1].Text);
        Assert.False(segments[2].IsMatch);
    }

    [Fact]
    public void Snippet_keeps_the_original_text_intact()
    {
        var snippet = $"{Mark("Интеграл")} Римана и {Mark("интегральная")} сумма";

        Assert.Equal("Интеграл Римана и интегральная сумма", CardHighlight.Flatten(CardHighlight.FromSnippet(snippet)));
    }

    [Fact]
    public void Unpaired_marker_is_dropped_without_losing_text()
    {
        var segments = CardHighlight.FromSnippet("Начало " + CardHighlight.MarkerEnd + "конец");

        Assert.Equal("Начало конец", CardHighlight.Flatten(segments));
        Assert.DoesNotContain(segments, x => x.IsMatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_snippet_gives_nothing(string? snippet)
    {
        Assert.Empty(CardHighlight.FromSnippet(snippet));
    }

    [Fact]
    public void Text_without_terms_is_one_plain_segment()
    {
        var segments = CardHighlight.FromTerms("Теорема Стокса", []);

        Assert.Single(segments);
        Assert.False(segments[0].IsMatch);
        Assert.Equal("Теорема Стокса", segments[0].Text);
    }

    [Fact]
    public void Prefix_term_highlights_the_start_of_a_word()
    {
        var segments = CardHighlight.FromTerms(
            "Интегрирование по частям",
            [new CardQueryTerm("интегр", CardQueryTermKind.Prefix)]);

        Assert.True(segments[0].IsMatch);
        Assert.Equal("Интегр", segments[0].Text);
        Assert.Equal("Интегрирование по частям", CardHighlight.Flatten(segments));
    }

    [Fact]
    public void Prefix_term_does_not_highlight_the_middle_of_a_word()
    {
        // «интеграл» внутри «полуинтеграла» — не то, что искали префиксным запросом.
        var segments = CardHighlight.FromTerms(
            "Полуинтеграл",
            [new CardQueryTerm("интеграл", CardQueryTermKind.Prefix)]);

        Assert.Single(segments);
        Assert.False(segments[0].IsMatch);
    }

    [Fact]
    public void Case_and_yo_are_folded_exactly_like_in_the_index()
    {
        var segments = CardHighlight.FromTerms(
            "Ёмкость конденсатора",
            [new CardQueryTerm("ЕМКОСТЬ", CardQueryTermKind.Prefix)]);

        Assert.True(segments[0].IsMatch);
        Assert.Equal("Ёмкость", segments[0].Text);
    }

    [Fact]
    public void Phrase_term_matches_anywhere_including_the_middle_of_a_word()
    {
        var segments = CardHighlight.FromTerms(
            "Несобственный интеграл",
            [new CardQueryTerm("собственный", CardQueryTermKind.Phrase)]);

        Assert.Contains(segments, x => x is { IsMatch: true, Text: "собственный" });
    }

    [Fact]
    public void Excluded_terms_are_never_highlighted()
    {
        var segments = CardHighlight.FromTerms(
            "Интеграл Римана",
            [new CardQueryTerm("римана", CardQueryTermKind.Exclude)]);

        Assert.DoesNotContain(segments, x => x.IsMatch);
    }

    [Fact]
    public void Overlapping_matches_are_merged_into_one_run()
    {
        var segments = CardHighlight.FromTerms(
            "Интеграл",
            [
                new CardQueryTerm("инте", CardQueryTermKind.Prefix),
                new CardQueryTerm("интеграл", CardQueryTermKind.Prefix),
            ]);

        Assert.Single(segments);
        Assert.True(segments[0].IsMatch);
        Assert.Equal("Интеграл", segments[0].Text);
    }

    [Fact]
    public void Long_text_is_truncated_with_an_ellipsis()
    {
        var segments = CardHighlight.FromTerms(new string('а', 300), [], maxLength: 100);
        var text = CardHighlight.Flatten(segments);

        Assert.Equal(101, text.Length);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncation_keeps_highlighting_of_what_survived()
    {
        var segments = CardHighlight.FromTerms(
            "Интеграл Римана и много другого текста, который в плитку уже не поместится",
            [new CardQueryTerm("интеграл", CardQueryTermKind.Prefix)],
            maxLength: 20);

        Assert.True(segments[0].IsMatch);
        Assert.EndsWith("…", CardHighlight.Flatten(segments), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(((")]
    [InlineData("\"незакрытая")]
    [InlineData("---")]
    [InlineData("  ")]
    [InlineData("")]
    public void Junk_input_never_throws(string junk)
    {
        var spec = CardQuery.Parse(junk);

        var fromTerms = CardHighlight.FromTerms(junk, spec.Terms);
        var fromSnippet = CardHighlight.FromSnippet(junk);

        Assert.NotNull(fromTerms);
        Assert.NotNull(fromSnippet);
    }

    [Fact]
    public void Segments_always_reassemble_into_the_source_text()
    {
        const string text = "Формула Ньютона-Лейбница связывает интеграл и первообразную";
        var spec = CardQuery.Parse("интеграл формула");

        Assert.Equal(text, CardHighlight.Flatten(CardHighlight.FromTerms(text, spec.Terms)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_text_gives_nothing(string? text)
    {
        Assert.Empty(CardHighlight.FromTerms(text, [new CardQueryTerm("что-то", CardQueryTermKind.Prefix)]));
    }
}
