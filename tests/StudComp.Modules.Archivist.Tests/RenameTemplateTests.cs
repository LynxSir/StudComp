using StudComp.Core.Domain;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>Токены шаблона переименования (ARCHITECTURE §8.4 п.4).</summary>
public sealed class RenameTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 14, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("{Subject}{Ext}", "Матан.docx")]
    [InlineData("{OriginalName}{Ext}", "отчёт.docx")]
    [InlineData("{Subject}_{OriginalName}{Ext}", "Матан_отчёт.docx")]
    [InlineData("{Subject}_{Date:yyyy-MM-dd}{Ext}", "Матан_2026-09-03.docx")]
    [InlineData("{Date}{Ext}", "20260903.docx")]
    [InlineData("{Subject}_{Type}{Ext}", "Матан_лекция.docx")]
    [InlineData("{Subject}_{Counter}{Ext}", "Матан_.docx")]
    public void Tokens_are_substituted(string template, string expected)
    {
        var result = RenameTemplate.Apply(template, Values("отчёт.docx"), KeywordRule("лекция"), "Матан", Now);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Empty_template_keeps_original_name()
    {
        var result = RenameTemplate.Apply(string.Empty, Values("отчёт.docx"), KeywordRule("лекция"), "Матан", Now);

        Assert.Equal("отчёт.docx", result);
    }

    [Fact]
    public void Extension_is_appended_when_template_forgot_it()
    {
        var result = RenameTemplate.Apply("{Subject}_{OriginalName}", Values("отчёт.docx"), KeywordRule("лекция"), "Матан", Now);

        Assert.Equal("Матан_отчёт.docx", result);
    }

    [Fact]
    public void Invalid_file_name_characters_are_replaced()
    {
        var result = RenameTemplate.Apply("{Subject}{Ext}", Values("отчёт.docx"), KeywordRule("лекция"), "Мат/ан:1", Now);

        Assert.Equal("Мат_ан_1.docx", result);
        Assert.DoesNotContain(result, c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Fact]
    public void Template_collapsing_to_nothing_falls_back_to_original_name()
    {
        // Предмета нет, тип пустой — имя из одних разделителей недопустимо.
        var rule = new ArchivistRule { MatchType = RuleMatchType.Regex, Pattern = "x" };

        var result = RenameTemplate.Apply("{Subject}_{Type}{Ext}", Values("отчёт.docx"), rule, string.Empty, Now);

        Assert.Equal("отчёт.docx", result);
    }

    [Fact]
    public void Extension_rule_uses_extension_as_type_label()
    {
        var rule = new ArchivistRule { MatchType = RuleMatchType.Extension, Pattern = ".docx" };

        var result = RenameTemplate.Apply("{Type}_{OriginalName}{Ext}", Values("отчёт.docx"), rule, "Матан", Now);

        Assert.Equal("docx_отчёт.docx", result);
    }

    [Fact]
    public void WorkType_field_wins_over_keyword_and_extension_for_Type_token()
    {
        var rule = new ArchivistRule { MatchType = RuleMatchType.Keyword, Pattern = "лекция", WorkType = "ЛР" };

        var result = RenameTemplate.Apply("{Subject}_{Type}_{Date:yyyy-MM-dd}{Ext}",
            Values("вариант7.docx"), rule, "Матан", Now);

        Assert.Equal("Матан_ЛР_2026-09-03.docx", result);
    }

    [Fact]
    public void Regex_rule_with_WorkType_gets_a_type_label()
    {
        var rule = new ArchivistRule { MatchType = RuleMatchType.Regex, Pattern = @"^ЛР\d+", WorkType = "ЛР" };

        var result = RenameTemplate.Apply("{Type}_{OriginalName}{Ext}", Values("ЛР1.docx"), rule, "Матан", Now);

        Assert.Equal("ЛР_ЛР1.docx", result);
    }

    [Fact]
    public void File_without_extension_is_handled()
    {
        var result = RenameTemplate.Apply("{Subject}_{OriginalName}{Ext}", Values("README"), KeywordRule("л"), "Матан", Now);

        Assert.Equal("Матан_README", result);
    }

    // --- Ревизия edge cases Phase 12: длина имени (ARCHITECTURE §8.8) ---

    [Fact]
    public void Overlong_original_name_is_truncated_but_extension_survives()
    {
        var longStem = new string('и', 400);
        var result = RenameTemplate.Apply(
            "{OriginalName}{Ext}", Values($"{longStem}.docx"), KeywordRule("л"), "Матан", Now);

        Assert.True(result.Length <= 255, $"Имя длиной {result.Length} превышает предел NTFS");
        // Оставлен запас под суффикс конфликта « (999)», который допишет исполнитель.
        Assert.True(result.Length <= 255 - 6);
        Assert.EndsWith(".docx", result);
        Assert.StartsWith("иии", result);
    }

    [Fact]
    public void Overlong_composed_name_from_template_is_clamped()
    {
        var longStem = new string('a', 400);
        var result = RenameTemplate.Apply(
            "{Subject}_{OriginalName}_{Date:yyyy-MM-dd}{Ext}",
            Values($"{longStem}.pdf"), KeywordRule("л"), "Матан", Now);

        Assert.True(result.Length <= 255 - 6);
        Assert.EndsWith(".pdf", result);
        Assert.DoesNotContain("__", result);
    }

    private static WatchedFileInfoValues Values(string fileName) =>
        new(fileName, Path.GetExtension(fileName).ToLowerInvariant());

    private static ArchivistRule KeywordRule(string pattern) =>
        new() { MatchType = RuleMatchType.Keyword, Pattern = pattern };
}
