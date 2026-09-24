using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Сравнение путей: на нём держатся область действия правила по папке и выборка записей по префиксу
/// папки в сверке (Phase 10).
/// </summary>
public sealed class PathComparisonTests
{
    [Theory]
    [InlineData(@"C:\Загрузки", @"C:\Загрузки", true)]
    [InlineData(@"C:\Загрузки\", @"C:\Загрузки", true)]
    [InlineData(@"c:\загрузки", @"C:\ЗАГРУЗКИ", true)]
    [InlineData(@"  C:\Загрузки  ", @"C:\Загрузки", true)]
    [InlineData(@"C:\Загрузки", @"C:\Загрузки (старое)", false)]
    [InlineData(@"C:\Загрузки", @"C:\Документы", false)]
    [InlineData("", @"C:\Загрузки", false)]
    public void Directories_are_compared_case_and_slash_insensitively(
        string left, string right, bool same)
    {
        Assert.Equal(same, PathComparison.SameDirectory(left, right));
    }

    [Theory]
    [InlineData(@"C:\Загрузки", @"C:\Загрузки\отчёт.docx", true)]
    [InlineData(@"C:\Загрузки\", @"C:\Загрузки\вложенная\отчёт.docx", true)]
    [InlineData(@"C:\загрузки", @"C:\ЗАГРУЗКИ\отчёт.docx", true)]
    // Ключевой случай: без хвостового разделителя «Загрузки» стала бы родителем «Загрузки (старое)».
    [InlineData(@"C:\Загрузки", @"C:\Загрузки (старое)\отчёт.docx", false)]
    [InlineData(@"C:\Загрузки", @"C:\Документы\отчёт.docx", false)]
    [InlineData(@"C:\Загрузки", @"C:\Загрузки", false)]
    public void Containment_requires_a_real_directory_boundary(
        string directory, string filePath, bool contains)
    {
        Assert.Equal(contains, PathComparison.Contains(directory, filePath));
    }

    [Fact]
    public void Normalize_strips_trailing_separator()
    {
        Assert.Equal(@"C:\Загрузки", PathComparison.Normalize(@"C:\Загрузки\"));
        Assert.Equal(@"C:\Загрузки", PathComparison.Normalize(@"C:\Загрузки"));
    }

    [Fact]
    public void Malformed_path_does_not_throw()
    {
        // Мусор в настройках не должен ронять конвейер — сравнение просто не совпадёт.
        var garbage = "C:\\Загрузки\\<>|\0";

        Assert.False(PathComparison.SameDirectory(garbage, @"C:\Загрузки"));
    }

    // --- Ревизия edge cases Phase 12: сетевые (UNC) и длинные пути (ARCHITECTURE §8.8) ---

    [Theory]
    [InlineData(@"\\nas\share\Загрузки", @"\\nas\share\Загрузки\", true)]
    [InlineData(@"\\NAS\Share\Загрузки", @"\\nas\share\ЗАГРУЗКИ", true)]
    [InlineData(@"\\nas\share\Загрузки", @"\\nas\share\Документы", false)]
    [InlineData(@"\\nas\share\a", @"\\nas\share\ab", false)]
    public void Unc_directories_are_compared_like_local_ones(string left, string right, bool same)
    {
        Assert.Equal(same, PathComparison.SameDirectory(left, right));
    }

    [Theory]
    [InlineData(@"\\nas\share\Загрузки", @"\\nas\share\Загрузки\вложенная\отчёт.docx", true)]
    // Тот же барьер каталога, что и для локальных путей: «a» не родитель «ab».
    [InlineData(@"\\nas\share\a", @"\\nas\share\ab\отчёт.docx", false)]
    [InlineData(@"\\nas\share\Загрузки", @"\\nas\other\Загрузки\отчёт.docx", false)]
    public void Unc_containment_requires_a_real_directory_boundary(
        string directory, string filePath, bool contains)
    {
        Assert.Equal(contains, PathComparison.Contains(directory, filePath));
    }

    [Fact]
    public void Extended_length_prefix_is_preserved_by_normalize()
    {
        var longName = new string('и', 300);
        var extended = $@"\\?\C:\Архив\{longName}";

        // Префикс \\?\ снимает лимит MAX_PATH — Normalize не должен ни падать, ни его терять.
        Assert.StartsWith(@"\\?\C:\Архив\", PathComparison.Normalize(extended));
        Assert.True(PathComparison.Contains($@"\\?\C:\Архив\{longName}", $@"\\?\C:\Архив\{longName}\файл.docx"));
    }
}
