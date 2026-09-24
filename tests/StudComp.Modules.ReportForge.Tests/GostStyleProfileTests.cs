using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Заводской профиль <c>gost-7.32-2017.json</c> — значения из таблицы ARCHITECTURE §10.4.
/// Тест стережёт файл: молчаливая правка профиля не должна пройти незамеченной.
/// </summary>
public class GostStyleProfileTests
{
    private static GostStyleProfile Profile => DocxTestHelpers.DefaultProfile;

    [Fact]
    public void Embedded_profile_is_readable()
    {
        Assert.False(string.IsNullOrWhiteSpace(DocxTestHelpers.DefaultProfileJson));
        Assert.NotNull(Profile);
    }

    [Fact]
    public void Body_text_matches_the_standard()
    {
        Assert.Equal("Times New Roman", Profile.FontFamily);
        Assert.Equal(14d, Profile.FontSizePt);
        Assert.Equal(1.5d, Profile.LineSpacing);
        Assert.Equal(1.25d, Profile.ParagraphIndentCm);
        Assert.Equal(ParagraphAlignment.Justify, Profile.BodyAlignment);
    }

    [Fact]
    public void Margins_match_the_standard()
    {
        Assert.Equal(30d, Profile.Margins.Left);
        Assert.Equal(15d, Profile.Margins.Right);
        Assert.Equal(20d, Profile.Margins.Top);
        Assert.Equal(20d, Profile.Margins.Bottom);
    }

    [Fact]
    public void Bullet_marker_is_a_dash()
    {
        Assert.Equal("–", Profile.BulletMarker);
    }

    [Fact]
    public void First_level_headings_start_a_new_page()
    {
        var first = Assert.Single(Profile.HeadingRules, rule => rule.Level == 1);

        Assert.True(first.Bold);
        Assert.True(first.PageBreakBefore);
        Assert.Equal(ParagraphAlignment.Center, first.Alignment);
    }

    [Fact]
    public void Deeper_headings_do_not_break_the_page()
    {
        Assert.All(
            Profile.HeadingRules.Where(rule => rule.Level > 1),
            rule => Assert.False(rule.PageBreakBefore));
    }

    [Fact]
    public void Page_numbering_is_at_the_bottom_and_skips_the_title()
    {
        Assert.True(Profile.PageNumbering.Enabled);
        Assert.Equal(PageNumberPosition.BottomCenter, Profile.PageNumbering.Position);
        Assert.True(Profile.PageNumbering.SkipTitlePage);
    }

    [Fact]
    public void Table_of_contents_covers_levels_one_to_three()
    {
        Assert.Equal(1, Profile.TableOfContents.MinLevel);
        Assert.Equal(3, Profile.TableOfContents.MaxLevel);
    }

    [Fact]
    public void Title_page_template_is_not_set_so_the_built_in_layout_is_used()
    {
        Assert.Null(Profile.TitlePageTemplate);
    }

    [Fact]
    public void Code_block_style_matches_the_defaults()
    {
        var code = Assert.IsType<CodeBlockStyleRule>(Profile.CodeBlock);

        Assert.Equal("Consolas", code.MonospaceFontFamily);
        Assert.Equal(-2d, code.RelativeFontSizePt);
        Assert.True(code.Boxed);
        Assert.Equal("F5F5F5", code.BackgroundHex);
    }
}
