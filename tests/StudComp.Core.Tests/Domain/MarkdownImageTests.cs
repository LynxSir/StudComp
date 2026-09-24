using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

public sealed class MarkdownImageTests
{
    [Fact]
    public void Examples_inside_code_are_not_treated_as_attachments()
    {
        const string source = "`![example](missing.png)`\n```md\n![example](missing2.png)\n```\n![real](photo.png)";
        Assert.Equal("photo.png", Assert.Single(MarkdownLocalImages.Paths(source)));
        var result = MarkdownLocalImages.Rewrite(source, path => "new/" + path);
        Assert.Contains("`![example](missing.png)`", result);
        Assert.Contains("![example](missing2.png)", result);
        Assert.Contains("![real](new/photo.png)", result);
    }

    [Fact]
    public void Image_links_preserve_spaces_hashes_and_parentheses()
    {
        const string path = "Математика/Рисунки/Фото #1 (2).png";
        var source = $"![Фото](<{MarkdownLocalImages.Encode(path)}>){{width=320}}";
        Assert.Equal(path, Assert.Single(MarkdownLocalImages.Paths(source)));
        var mapped = MarkdownLocalImages.Rewrite(source, x => "../" + x);
        Assert.Equal("../" + path, Assert.Single(MarkdownLocalImages.Paths(mapped)));
        Assert.EndsWith("{width=320}", mapped);
    }

    [Fact]
    public void Resizing_one_occurrence_keeps_other_images_and_attributes()
    {
        const string link = "![a](image.png)";
        var source = link + "\n" + link + "{width=200 #photo}";
        var resized = MarkdownImageSize.SetWidth(source, link.Length + 1, link.Length, 480);
        Assert.StartsWith(link + "\n" + link, resized);
        Assert.Contains("#photo", resized);
        Assert.DoesNotContain("width=200", resized);
        Assert.Contains("width=480", resized);
        var automatic = MarkdownImageSize.SetWidth(resized, link.Length + 1, link.Length, null);
        Assert.DoesNotContain("width=", automatic);
    }
}
