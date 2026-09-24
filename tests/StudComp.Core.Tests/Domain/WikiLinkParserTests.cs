using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты разбора wiki-ссылок <c>[[…]]</c> в обороте карточки (new_addons.md §7, Phase 12.9).
/// Вход — текст, написанный человеком, поэтому главный инвариант — «ни одного исключения на любой строке».
/// </summary>
public sealed class WikiLinkParserTests
{
    [Fact]
    public void A_single_link_is_recognized()
    {
        var parsed = WikiLinkParser.Parse("См. также [[Теорема Стокса]].");

        Assert.True(parsed.HasLinks);
        Assert.Equal(
            [
                new WikiLinkSegment(WikiLinkSegmentKind.Text, "См. также "),
                new WikiLinkSegment(WikiLinkSegmentKind.Link, "[[Теорема Стокса]]", "Теорема Стокса"),
                new WikiLinkSegment(WikiLinkSegmentKind.Text, "."),
            ],
            parsed.Segments);
    }

    [Fact]
    public void Several_links_in_one_text_are_all_found()
    {
        var parsed = WikiLinkParser.Parse("[[А]] и [[Б]] и [[В]]");

        var links = parsed.Segments.Where(s => s.Kind == WikiLinkSegmentKind.Link).Select(s => s.Target).ToList();
        Assert.Equal(["А", "Б", "В"], links);
    }

    [Fact]
    public void A_link_at_the_very_start_and_end_is_fine()
    {
        var parsed = WikiLinkParser.Parse("[[начало]] середина [[конец]]");

        Assert.Equal(3, parsed.Segments.Count);
        Assert.Equal(WikiLinkSegmentKind.Link, parsed.Segments[0].Kind);
        Assert.Equal(WikiLinkSegmentKind.Link, parsed.Segments[^1].Kind);
    }

    [Fact]
    public void Target_is_trimmed()
    {
        var parsed = WikiLinkParser.Parse("[[  Теорема Стокса  ]]");

        var link = parsed.Segments.Single(s => s.Kind == WikiLinkSegmentKind.Link);
        Assert.Equal("Теорема Стокса", link.Target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("обычный текст без ссылок")]
    [InlineData("[[ незакрытая скобка")]
    [InlineData("одинокая закрывающая ]] скобка")]
    [InlineData("[[]]")]
    [InlineData("[[   ]]")]
    [InlineData("[ одинарные ] скобки")]
    [InlineData("]][[")]
    public void Text_without_valid_links_has_none(string input)
    {
        var parsed = WikiLinkParser.Parse(input);

        Assert.False(parsed.HasLinks);
    }

    [Fact]
    public void An_unclosed_bracket_keeps_the_rest_as_plain_text()
    {
        var parsed = WikiLinkParser.Parse("начало [[ и дальше без закрытия");

        var only = Assert.Single(parsed.Segments);
        Assert.Equal(WikiLinkSegmentKind.Text, only.Kind);
        Assert.Equal("начало [[ и дальше без закрытия", only.Text);
    }

    [Fact]
    public void Nesting_is_not_supported_and_closes_on_the_first_double_bracket()
    {
        var parsed = WikiLinkParser.Parse("[[внешний [[внутренний]] хвост]]");

        var link = parsed.Segments.First(s => s.Kind == WikiLinkSegmentKind.Link);
        Assert.Equal("внешний [[внутренний", link.Target);
    }

    [Fact]
    public void Null_is_treated_as_empty()
    {
        var parsed = WikiLinkParser.Parse(null);

        Assert.False(parsed.HasLinks);
        Assert.Empty(parsed.Segments);
    }

    [Theory]
    [InlineData("[[а]]", true)]
    [InlineData("[[   ]]", false)]
    [InlineData("нет ссылок", false)]
    [InlineData(null, false)]
    public void Contains_links_answers_without_building_segments(string? input, bool expected)
    {
        Assert.Equal(expected, WikiLinkParser.ContainsLinks(input));
    }

    [Fact]
    public void Garbage_input_never_throws()
    {
        string[] garbage =
        [
            "[[[[[[", "]]]]]]", "[[]][[]]", new string('[', 500), "[[\n]]", "[[ ]]", "[[ё]]",
        ];

        foreach (var value in garbage)
        {
            var parsed = WikiLinkParser.Parse(value);
            Assert.NotNull(parsed.Segments);
        }
    }

    [Fact]
    public void The_raw_text_is_kept_as_given()
    {
        const string source = "[[а]] хвост";

        Assert.Equal(source, WikiLinkParser.Parse(source).Raw);
    }
}
