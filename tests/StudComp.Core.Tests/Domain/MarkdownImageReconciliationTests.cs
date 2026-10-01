using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Сверка ссылок — единственный механизм, по которому файл следует за текстом, включая отмену.
/// </summary>
public sealed class MarkdownImageReconciliationTests
{
    private static ImageReferenceDelta Between(string before, string after) =>
        MarkdownImageReconciliation.Compare(
            MarkdownImageReconciliation.CountReferences(before),
            MarkdownImageReconciliation.CountReferences(after));

    [Fact]
    public void Removing_one_of_two_occurrences_detaches_nothing()
    {
        const string before = "![a](a.png)\ntext\n![a](a.png)";
        const string after = "![a](a.png)\ntext";

        var delta = Between(before, after);

        Assert.Empty(delta.Detached);
        Assert.Empty(delta.Reattached);
    }

    [Fact]
    public void Removing_the_last_occurrence_detaches_the_file()
    {
        var delta = Between("![a](a.png)\ntext", "text");

        Assert.Equal("a.png", Assert.Single(delta.Detached));
        Assert.Empty(delta.Reattached);
    }

    [Fact]
    public void Undo_is_symmetric()
    {
        const string withImage = "text\n![a](a.png)";
        const string without = "text";

        Assert.Equal("a.png", Assert.Single(Between(withImage, without).Detached));
        Assert.Equal("a.png", Assert.Single(Between(without, withImage).Reattached));
    }

    [Fact]
    public void Case_and_separators_denote_the_same_file()
    {
        var delta = Between(
            "![a](<" + MarkdownLocalImages.Encode("Матан/Рисунки/A.png") + ">)",
            "![a](<" + MarkdownLocalImages.Encode("матан/рисунки/a.PNG") + ">)");

        Assert.Empty(delta.Detached);
        Assert.Empty(delta.Reattached);
    }

    [Fact]
    public void Moving_an_image_within_the_note_detaches_nothing()
    {
        const string source = "one\n![a](a.png)\ntwo";
        var token = MarkdownLocalImages.Tokens(source)[0];
        var moved = MarkdownEditing.Apply(source, MarkdownImageEditing.MoveToken(source, token, source.Length)!.Value);

        var delta = Between(source, moved);

        Assert.Empty(delta.Detached);
        Assert.Empty(delta.Reattached);
    }

    [Fact]
    public void Replacing_one_image_with_another_reports_both_sides()
    {
        var delta = Between("![a](a.png)", "![b](b.png)");

        Assert.Equal("a.png", Assert.Single(delta.Detached));
        Assert.Equal("b.png", Assert.Single(delta.Reattached));
    }

    [Fact]
    public void An_example_inside_a_code_fence_is_not_a_reference()
    {
        const string before = "```md\n![a](a.png)\n```";
        const string after = "```md\n![a](a.png)\n```\nnew text";

        Assert.Empty(MarkdownImageReconciliation.CountReferences(before));
        Assert.Empty(Between(before, after).Detached);
    }

    [Fact]
    public void Remote_images_are_never_counted() =>
        Assert.Empty(MarkdownImageReconciliation.CountReferences("![a](https://example.com/a.png)"));

    [Fact]
    public void Reference_counts_are_reported_per_path()
    {
        var counts = MarkdownImageReconciliation.CountReferences("![a](a.png)![a](a.png)![b](b.png)");

        Assert.Equal(2, counts["a.png"]);
        Assert.Equal(1, counts["b.png"]);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("обычный текст", false)]
    [InlineData("![a](a.png)", true)]
    public void The_short_circuit_only_passes_text_that_may_hold_a_link(string? source, bool expected) =>
        Assert.Equal(expected, MarkdownImageReconciliation.MightContainImages(source));

    [Fact]
    public void Comparing_against_nothing_never_throws()
    {
        var counts = MarkdownImageReconciliation.CountReferences("![a](a.png)");

        Assert.Equal("a.png", Assert.Single(MarkdownImageReconciliation.Compare(counts, null).Detached));
        Assert.Equal("a.png", Assert.Single(MarkdownImageReconciliation.Compare(null, counts).Reattached));
    }
}
