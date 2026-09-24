using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Табличные тесты движка правил (ARCHITECTURE §12): вход — имя файла и набор правил, выход —
/// ожидаемое решение. Диск здесь не участвует вообще.
/// </summary>
public sealed class SortingRuleEngineTests : ArchivistDatabaseTestBase
{
    [Theory]
    [InlineData("docx", true)]
    [InlineData(".docx", true)]
    [InlineData("*.docx", true)]
    [InlineData(".DOCX", true)]
    [InlineData(".pdf", false)]
    public async Task Extension_rule_accepts_common_pattern_forms(string pattern, bool shouldMatch)
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        var rule = Rule(pattern, RuleMatchType.Extension, subject.Id);

        var decision = await Engine.EvaluateAsync(File("отчёт.docx"), [rule], CancellationToken.None);

        Assert.Equal(shouldMatch, decision is not null);
    }

    [Theory]
    [InlineData("лекция", true)]
    [InlineData("ЛЕКЦИЯ", true)]
    [InlineData("семинар", false)]
    public async Task Keyword_rule_matches_substring_ignoring_case(string pattern, bool shouldMatch)
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        var rule = Rule(pattern, RuleMatchType.Keyword, subject.Id);

        var decision = await Engine.EvaluateAsync(File("Лекция 3 про пределы.pdf"), [rule], CancellationToken.None);

        Assert.Equal(shouldMatch, decision is not null);
    }

    [Fact]
    public async Task Higher_priority_rule_wins()
    {
        var lowSubject = await SeedSubjectAsync("Общая папка", @"C:\Учёба\Прочее");
        var highSubject = await SeedSubjectAsync("Матан", @"C:\Учёба\Матан");

        var low = Rule(".pdf", RuleMatchType.Extension, lowSubject.Id, priority: 1);
        var high = Rule("лекция", RuleMatchType.Keyword, highSubject.Id, priority: 100);

        // Порядок в списке намеренно обратный приоритету — движок обязан отсортировать сам.
        var decision = await Engine.EvaluateAsync(File("Лекция 1.pdf"), [low, high], CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(highSubject.Id, decision.SubjectId);
    }

    [Fact]
    public async Task Disabled_rule_is_skipped()
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        var rule = Rule(".docx", RuleMatchType.Extension, subject.Id);
        rule.Enabled = false;

        var decision = await Engine.EvaluateAsync(File("отчёт.docx"), [rule], CancellationToken.None);

        Assert.Null(decision);
    }

    [Fact]
    public async Task No_matching_rule_returns_null()
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        var rule = Rule(".docx", RuleMatchType.Extension, subject.Id);

        var decision = await Engine.EvaluateAsync(File("архив.zip"), [rule], CancellationToken.None);

        Assert.Null(decision);
    }

    [Theory]
    [InlineData(@"^ЛР\d+_.*\.docx$", "ЛР1_отчёт.docx", true)]
    [InlineData(@"^ЛР\d+_.*\.docx$", "КР1_отчёт.docx", false)]
    [InlineData(@"^лр\d+", "ЛР7_вариант.docx", true)] // регистр не важен
    public async Task Regex_rule_matches_by_full_file_name(string pattern, string fileName, bool shouldMatch)
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        var rule = Rule(pattern, RuleMatchType.Regex, subject.Id);

        var decision = await Engine.EvaluateAsync(File(fileName), [rule], CancellationToken.None);

        Assert.Equal(shouldMatch, decision is not null);
    }

    [Fact]
    public async Task Invalid_regex_pattern_is_skipped_not_thrown()
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        var rule = Rule("([", RuleMatchType.Regex, subject.Id);

        var decision = await Engine.EvaluateAsync(File("что-угодно.docx"), [rule], CancellationToken.None);

        Assert.Null(decision);
    }

    [Fact]
    public async Task Catastrophic_regex_times_out_within_budget_and_is_skipped()
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        Options.CurrentValue = new ArchivistOptions { RegexMatchTimeoutMs = 50 };
        // Классический «злой» паттерн: экспоненциальный бэктрекинг на длинной строке без завершающего '!'.
        var rule = Rule(@"^(a+)+$", RuleMatchType.Regex, subject.Id);
        var evil = new string('a', 50) + "b.docx";

        var started = DateTimeOffset.UtcNow;
        var decision = await Engine.EvaluateAsync(File(evil), [rule], CancellationToken.None);

        Assert.Null(decision);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Subject_folder_is_the_primary_target()
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        Options.CurrentValue = new ArchivistOptions { ArchiveRootFolder = @"C:\Архив" };

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subject.Id)],
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(@"C:\Учёба\Матан", decision.TargetDirectory);
    }

    [Fact]
    public async Task Archive_root_is_used_when_subject_has_no_folder()
    {
        var subject = await SeedSubjectAsync("Дискретка");
        Options.CurrentValue = new ArchivistOptions { ArchiveRootFolder = @"C:\Архив" };

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subject.Id)],
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(Path.Combine(@"C:\Архив", "Дискретка"), decision.TargetDirectory);
    }

    [Fact]
    public async Task Rule_without_subject_targets_archive_root()
    {
        Options.CurrentValue = new ArchivistOptions { ArchiveRootFolder = @"C:\Архив" };

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subjectId: null)],
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Null(decision.SubjectId);
        Assert.Equal(@"C:\Архив", decision.TargetDirectory);
    }

    /// <summary>
    /// Phase 13.2, главный смысл фазы: корень архива не задан — цель считается от учебной папки,
    /// ровно та же, что показывает Хаб предмета. До этой фазы файл остался бы неразобранным.
    /// </summary>
    [Fact]
    public async Task Study_root_is_the_target_when_no_archive_root_is_set()
    {
        var subject = await SeedSubjectAsync("Дискретка");
        WorkspaceOptions.CurrentValue = new WorkspaceOptions { StudyRootPath = @"C:\Учёба" };

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subject.Id)],
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(Path.Combine(@"C:\Учёба", "Дискретка"), decision.TargetDirectory);
        Assert.Equal(Workspace.GetSubjectDirectory(subject), decision.TargetDirectory);
    }

    /// <summary>Имя подпапки из формы предмета — обычный случай хранения с Phase 13.2.</summary>
    [Fact]
    public async Task Relative_folder_name_is_resolved_inside_the_study_root()
    {
        var subject = await SeedSubjectAsync("Математический анализ", folderPath: "МА-2");
        WorkspaceOptions.CurrentValue = new WorkspaceOptions { StudyRootPath = @"C:\Учёба" };

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subject.Id)],
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(Path.Combine(@"C:\Учёба", "МА-2"), decision.TargetDirectory);
    }

    /// <summary>
    /// Явно заданный корень архива сильнее учебной папки: у кого архив исторически лежит отдельно,
    /// файлы продолжают ехать туда же, куда ехали до обновления (§14 — не уводить файлы молча).
    /// </summary>
    [Fact]
    public async Task Archive_root_overrides_the_study_root()
    {
        var subject = await SeedSubjectAsync("Дискретка");
        WorkspaceOptions.CurrentValue = new WorkspaceOptions { StudyRootPath = @"C:\Учёба" };
        Options.CurrentValue = new ArchivistOptions { ArchiveRootFolder = @"C:\Архив" };

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subject.Id)],
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(Path.Combine(@"C:\Архив", "Дискретка"), decision.TargetDirectory);
    }

    /// <summary>Своя папка предмета сильнее обоих корней.</summary>
    [Fact]
    public async Task Custom_subject_folder_beats_both_roots()
    {
        var subject = await SeedSubjectAsync("Матан", folderPath: @"D:\Своё\Матан");
        WorkspaceOptions.CurrentValue = new WorkspaceOptions { StudyRootPath = @"C:\Учёба" };
        Options.CurrentValue = new ArchivistOptions { ArchiveRootFolder = @"C:\Архив" };

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subject.Id)],
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(@"D:\Своё\Матан", decision.TargetDirectory);
    }

    /// <summary>Правило без предмета при пустом корне архива кладёт в саму учебную папку.</summary>
    [Fact]
    public async Task Rule_without_subject_targets_the_study_root()
    {
        WorkspaceOptions.CurrentValue = new WorkspaceOptions { StudyRootPath = @"C:\Учёба" };

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subjectId: null)],
            CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(@"C:\Учёба", decision.TargetDirectory);
    }

    [Fact]
    public async Task No_folder_and_no_archive_root_leaves_file_unsorted()
    {
        var subject = await SeedSubjectAsync("Дискретка");

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx"),
            [Rule(".docx", RuleMatchType.Extension, subject.Id)],
            CancellationToken.None);

        Assert.Null(decision);
    }

    [Fact]
    public async Task Rename_template_is_applied_to_decision()
    {
        var subject = await SeedSubjectAsync("Матан", @"C:\Учёба\Матан");
        var rule = Rule(".docx", RuleMatchType.Extension, subject.Id);
        rule.RenameTemplate = "{Subject}_{OriginalName}{Ext}";

        var decision = await Engine.EvaluateAsync(File("отчёт.docx"), [rule], CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("Матан_отчёт.docx", decision.NewFileName);
    }

    // --- Phase 10: область действия правила по наблюдаемой папке (ADR §16.54) ---

    [Theory]
    [InlineData(null, @"C:\Загрузки", true)]
    [InlineData(@"C:\Загрузки", @"C:\Загрузки", true)]
    [InlineData(@"C:\Загрузки\", @"C:\Загрузки", true)]
    [InlineData(@"c:\загрузки", @"C:\Загрузки", true)]
    [InlineData(@"C:\Рабочий стол", @"C:\Загрузки", false)]
    [InlineData(@"C:\Загрузки", @"C:\Рабочий стол", false)]
    public async Task Rule_applies_only_inside_its_watched_folder(
        string? ruleFolder, string fileFolder, bool shouldMatch)
    {
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");
        var rule = Rule(".docx", RuleMatchType.Extension, subject.Id);
        rule.WatchedFolder = ruleFolder;

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx", fileFolder), [rule], CancellationToken.None);

        Assert.Equal(shouldMatch, decision is not null);
    }

    [Fact]
    public async Task Folder_scope_is_checked_before_priority()
    {
        // Правило с высшим приоритетом чужой папки не должно перехватывать файл у правила своей.
        var subject = await SeedSubjectAsync(folderPath: @"C:\Учёба\Матан");

        var foreign = Rule(".docx", RuleMatchType.Extension, subject.Id, priority: 100);
        foreign.WatchedFolder = @"C:\Рабочий стол";
        foreign.RenameTemplate = "чужое{Ext}";

        var own = Rule(".docx", RuleMatchType.Extension, subject.Id, priority: 10);
        own.WatchedFolder = @"C:\Загрузки";
        own.RenameTemplate = "своё{Ext}";

        var decision = await Engine.EvaluateAsync(
            File("отчёт.docx", @"C:\Загрузки"), [foreign, own], CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("своё.docx", decision.NewFileName);
    }

    private static WatchedFileInfo File(string fileName, string watchedFolder = @"C:\Загрузки") => new(
        FullPath: Path.Combine(watchedFolder, fileName),
        FileName: fileName,
        Extension: Path.GetExtension(fileName).ToLowerInvariant(),
        SizeBytes: 4096,
        CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
        ModifiedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
        WatchedFolderPath: watchedFolder);

    private static ArchivistRule Rule(string pattern, RuleMatchType matchType, Guid? subjectId, int priority = 10) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        Pattern = pattern,
        MatchType = matchType,
        Priority = priority,
        Enabled = true,
        RenameTemplate = string.Empty,
    };
}
