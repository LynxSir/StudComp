using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Границы вставки картинки. Сканер тут тот же, что у <see cref="MarkdownLocalImages.Paths"/>,
/// поэтому гарантия «пример внутри кода картинкой не считается» обязана выжить после рефакторинга.
/// </summary>
public sealed class MarkdownImageTokenTests
{
    [Fact]
    public void Token_length_covers_the_trailing_width_attribute()
    {
        const string source = "![a](photo.png){width=320}";
        var token = Assert.Single(MarkdownLocalImages.Tokens(source));

        Assert.Equal(0, token.Start);
        Assert.Equal(source.Length, token.Length);
        Assert.Equal("![a](photo.png)".Length, token.LinkLength);
        Assert.True(token.HasAttributes);
    }

    [Fact]
    public void Token_without_attributes_has_equal_lengths()
    {
        var token = Assert.Single(MarkdownLocalImages.Tokens("![a](photo.png)"));

        Assert.Equal(token.Length, token.LinkLength);
        Assert.False(token.HasAttributes);
    }

    [Fact]
    public void The_whole_attribute_block_is_absorbed()
    {
        const string source = "![a](photo.png){#anchor width=200 .cls}";
        var token = Assert.Single(MarkdownLocalImages.Tokens(source));

        Assert.Equal(source.Length, token.Length);
    }

    [Fact]
    public void An_attribute_block_separated_by_a_space_does_not_belong_to_the_token()
    {
        const string source = "![a](photo.png) {width=200}";
        var token = Assert.Single(MarkdownLocalImages.Tokens(source));

        Assert.Equal("![a](photo.png)".Length, token.Length);
        Assert.False(token.HasAttributes);
    }

    [Fact]
    public void Examples_inside_code_are_not_tokens()
    {
        const string source = "`![x](a.png)`\n```md\n![x](b.png)\n```\n![real](c.png)";
        var token = Assert.Single(MarkdownLocalImages.Tokens(source));

        Assert.Equal("c.png", token.Path);
    }

    [Fact]
    public void An_angle_bracket_path_is_decoded()
    {
        const string path = "Математика/Рисунки/Фото #1 (2).png";
        var source = $"![Фото](<{MarkdownLocalImages.Encode(path)}>){{width=320}}";
        var token = Assert.Single(MarkdownLocalImages.Tokens(source));

        Assert.Equal(path, token.Path);
        Assert.True(token.IsLocal);
        Assert.Equal(source.Length, token.Length);
    }

    [Fact]
    public void A_remote_image_is_a_token_but_not_local()
    {
        var token = Assert.Single(MarkdownLocalImages.Tokens("![a](http://example.com/a.png)"));

        Assert.False(token.IsLocal);
    }

    [Fact]
    public void Two_images_on_one_line_are_two_tokens()
    {
        var tokens = MarkdownLocalImages.Tokens("![a](a.png)![b](b.png)");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(tokens[0].End, tokens[1].Start);
    }

    [Fact]
    public void The_same_path_twice_gives_two_tokens()
    {
        var tokens = MarkdownLocalImages.Tokens("![a](a.png)\ntext\n![a](a.png)");

        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, token => Assert.Equal("a.png", token.Path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("![unclosed](a.png")]
    [InlineData("![](){}")]
    public void Malformed_input_never_throws(string? source) =>
        Assert.NotNull(MarkdownLocalImages.Tokens(source));

    [Fact]
    public void Boundaries_are_not_inside_the_token()
    {
        var token = Assert.Single(MarkdownLocalImages.Tokens("![a](a.png)"));

        Assert.False(token.Contains(token.Start));
        Assert.False(token.Contains(token.End));
        Assert.True(token.Contains(token.Start + 1));
        Assert.True(token.IsBoundary(token.Start));
        Assert.True(token.IsBoundary(token.End));
    }
}
