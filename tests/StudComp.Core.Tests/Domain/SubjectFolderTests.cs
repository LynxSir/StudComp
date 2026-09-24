using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Тесты <see cref="SubjectFolder"/> (Phase 13.2) — единственного места, где считается путь к папке
/// предмета. Раньше эта логика жила в трёх копиях, и расхождение между ними было тихим: Хаб показывал
/// одну папку, Архивариус раскладывал в другую.
/// </summary>
public sealed class SubjectFolderTests
{
    private static Subject Named(string name, string folderPath = "") =>
        new() { Id = Guid.NewGuid(), Name = name, FolderPath = folderPath };

    [Theory]
    [InlineData("Матан", "Матан")]
    [InlineData("  Матан  ", "Матан")]
    [InlineData("Мат/Анализ", "Мат_Анализ")]
    [InlineData(@"Мат\Анализ", "Мат_Анализ")]
    [InlineData("Мат:Анализ*?", "Мат_Анализ__")]
    [InlineData("Матан.", "Матан")]
    [InlineData("Матан ", "Матан")]
    [InlineData("", "Без названия")]
    [InlineData("   ", "Без названия")]
    [InlineData("...", "Без названия")]
    public void Sanitize_always_returns_one_safe_segment(string input, string expected)
    {
        var result = SubjectFolder.Sanitize(input);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, result);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, result);
    }

    [Fact]
    public void Sanitize_tolerates_null()
    {
        Assert.Equal("Без названия", SubjectFolder.Sanitize(null));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Матан", false)]
    [InlineData("Матан/Лекции", false)]
    [InlineData(@"C:\Учёба\Матан", true)]
    [InlineData(@"\server\share\Матан", true)]
    public void IsCustomPath_recognizes_only_rooted_values(string? folderPath, bool expected)
    {
        Assert.Equal(expected, SubjectFolder.IsCustomPath(folderPath));
    }

    [Fact]
    public void FolderName_falls_back_to_the_subject_name()
    {
        Assert.Equal("Матан", SubjectFolder.FolderName(Named("Матан")));
    }

    [Fact]
    public void FolderName_prefers_the_stored_relative_name()
    {
        Assert.Equal("МА-2", SubjectFolder.FolderName(Named("Матан", "МА-2")));
    }

    [Fact]
    public void FolderName_of_a_custom_path_is_its_last_segment()
    {
        Assert.Equal("Матан", SubjectFolder.FolderName(Named("Математический анализ", @"D:\Своё\Матан")));
        Assert.Equal("Матан", SubjectFolder.FolderName(Named("Математический анализ", @"D:\Своё\Матан\")));
    }

    [Fact]
    public void Resolve_combines_the_study_root_with_the_folder_name()
    {
        Assert.Equal(
            Path.Combine(@"C:\Учёба", "Матан"),
            SubjectFolder.Resolve(@"C:\Учёба", Named("Матан")));

        Assert.Equal(
            Path.Combine(@"C:\Учёба", "МА-2"),
            SubjectFolder.Resolve(@"C:\Учёба", Named("Матан", "МА-2")));
    }

    [Fact]
    public void Resolve_keeps_a_custom_path_untouched_even_when_a_root_is_set()
    {
        Assert.Equal(@"D:\Своё\Матан", SubjectFolder.Resolve(@"C:\Учёба", Named("Матан", @"D:\Своё\Матан")));
    }

    [Fact]
    public void Resolve_returns_empty_without_a_root()
    {
        Assert.Equal(string.Empty, SubjectFolder.Resolve("", Named("Матан")));
        Assert.Equal(string.Empty, SubjectFolder.Resolve(null, Named("Матан", "МА-2")));
        Assert.Equal(string.Empty, SubjectFolder.Resolve("   ", Named("Матан")));
    }

    [Fact]
    public void Resolve_never_escapes_the_study_root_through_a_crafted_name()
    {
        // Имя подпапки приходит из пользовательского ввода — разделители в нём обязаны быть обезврежены,
        // иначе «..\..\Windows» увёл бы раскладку файлов за пределы учебной папки.
        var resolved = SubjectFolder.Resolve(@"C:\Учёба", Named("Матан", @"..\..\Windows"));

        // Точка — допустимый символ имени файла, обезвреживаются именно разделители, и этого
        // достаточно: «..\..\Windows» превращается в безобидное имя одной подпапки.
        Assert.Equal(Path.Combine(@"C:\Учёба", ".._.._Windows"), resolved);
        Assert.StartsWith(@"C:\Учёба\", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_of_an_unnamed_subject_still_produces_a_usable_path()
    {
        Assert.Equal(Path.Combine(@"C:\Учёба", "Без названия"), SubjectFolder.Resolve(@"C:\Учёба", Named("  ")));
    }
}
