using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>Фильтрация шума перед разбором (ARCHITECTURE §8.4 п.1).</summary>
public sealed class IgnoreListTests
{
    private static readonly ArchivistOptions Defaults = new();

    [Theory]
    [InlineData("загрузка.tmp", true)]
    [InlineData("файл.crdownload", true)]
    [InlineData("~$отчёт.docx", true)]
    [InlineData("Thumbs.db", true)]
    [InlineData("ярлык.lnk", true)]
    [InlineData("отчёт.docx", false)]
    [InlineData("лекция.pdf", false)]
    public void Ignore_patterns_match_expected_names(string fileName, bool ignored)
    {
        Assert.Equal(ignored, IgnoreListMatcher.IsIgnoredName(fileName, Defaults.IgnoredPatterns));
    }

    [Fact]
    public void Small_and_fresh_file_is_postponed()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(IgnoreListMatcher.IsTooFreshAndSmall(100, now.AddMilliseconds(-500), now, Defaults));
    }

    [Fact]
    public void Small_but_old_file_is_processed()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(IgnoreListMatcher.IsTooFreshAndSmall(100, now.AddMinutes(-5), now, Defaults));
    }

    [Fact]
    public void Large_and_fresh_file_is_processed()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(IgnoreListMatcher.IsTooFreshAndSmall(1_000_000, now, now, Defaults));
    }

    [Theory]
    [InlineData(FileAttributes.Hidden, true)]
    [InlineData(FileAttributes.System, true)]
    [InlineData(FileAttributes.Directory, true)]
    [InlineData(FileAttributes.Normal, false)]
    public void System_and_hidden_entries_are_skipped(FileAttributes attributes, bool skipped)
    {
        Assert.Equal(skipped, IgnoreListMatcher.IsSystemOrHidden(attributes));
    }

    /// <summary>
    /// Нематериализованные облачные файлы (ARCHITECTURE §8.5): читать их нельзя — чтение заставит
    /// провайдер выкачать файл целиком, а переносить недокачанный файл тем более нечего.
    /// </summary>
    [Theory]
    [InlineData(FileAttributes.Offline, true)]
    [InlineData(FileAttributes.ReparsePoint, true)]
    [InlineData(IgnoreListMatcher.RecallOnOpen, true)]
    [InlineData(IgnoreListMatcher.RecallOnDataAccess, true)]
    [InlineData(FileAttributes.Normal, false)]
    [InlineData(FileAttributes.Archive, false)]
    [InlineData(FileAttributes.ReadOnly, false)]
    public void Cloud_placeholders_are_recognised(FileAttributes attributes, bool placeholder)
    {
        Assert.Equal(placeholder, IgnoreListMatcher.IsCloudPlaceholder(attributes));
    }

    [Fact]
    public void Ordinary_file_with_several_attributes_is_not_a_placeholder()
    {
        Assert.False(IgnoreListMatcher.IsCloudPlaceholder(
            FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.NotContentIndexed));
    }
}
