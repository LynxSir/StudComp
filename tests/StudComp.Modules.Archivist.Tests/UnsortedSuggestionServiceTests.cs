using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Подсказки предметов-кандидатов для «Неразобранного» (ARCHITECTURE §8.4 п.7, new_addons.md §4).
/// Чистая функция без БД, поэтому тесты табличные — тот же стиль, что <c>RenameTemplateTests</c>.
/// </summary>
public sealed class UnsortedSuggestionServiceTests
{
    private static Subject MakeSubject(string name, string code = "") =>
        new() { Id = Guid.NewGuid(), Name = name, Code = code };

    private static ArchivistRule KeywordRule(Guid subjectId, string pattern, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        Pattern = pattern,
        MatchType = RuleMatchType.Keyword,
        Enabled = enabled,
    };

    private static UnsortedSuggestionService Make(
        bool enabled = true, double minScore = 0.35, int maxCandidates = 3) =>
        new(new TestOptionsMonitor<ArchivistOptions>(new ArchivistOptions
        {
            UnsortedSuggestionsEnabled = enabled,
            UnsortedSuggestionMinScore = minScore,
            UnsortedSuggestionMaxCandidates = maxCandidates,
        }));

    [Fact]
    public void No_subjects_returns_empty()
    {
        var service = Make();

        var result = service.Suggest("ЛР4_Матан.docx", [], []);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_file_name_returns_empty(string fileName)
    {
        var service = Make();
        var subjects = new[] { MakeSubject("Математический анализ", "Матан") };

        Assert.Empty(service.Suggest(fileName, subjects, []));
    }

    [Fact]
    public void No_token_overlap_returns_empty()
    {
        var service = Make();
        var subjects = new[] { MakeSubject("Физика", "Физ") };

        var result = service.Suggest("отпускные_фото.zip", subjects, []);

        Assert.Empty(result);
    }

    [Fact]
    public void Matches_by_subject_code_even_when_name_does_not_overlap()
    {
        var service = Make(minScore: 0.1);
        var matan = MakeSubject("Математический анализ", "матан");

        var result = service.Suggest("лр4_матан.docx", [matan], []);

        Assert.Contains(result, s => s.SubjectId == matan.Id);
    }

    [Fact]
    public void Matches_via_enabled_keyword_rule_even_when_subject_name_does_not_overlap()
    {
        var service = Make(minScore: 0.1);
        var subject = MakeSubject("Математический анализ", "МатАн");
        var rule = KeywordRule(subject.Id, "конспект");

        var result = service.Suggest("конспект_лекции.docx", [subject], [rule]);

        Assert.Contains(result, s => s.SubjectId == subject.Id);
    }

    [Fact]
    public void Disabled_rule_does_not_contribute()
    {
        var service = Make(minScore: 0.1);
        var subject = MakeSubject("Физика");
        var rule = KeywordRule(subject.Id, "конспект", enabled: false);

        var result = service.Suggest("конспект_лекции.docx", [subject], [rule]);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(RuleMatchType.Extension)]
    [InlineData(RuleMatchType.Regex)]
    public void Non_keyword_rule_does_not_contribute(RuleMatchType matchType)
    {
        var service = Make(minScore: 0.1);
        var subject = MakeSubject("Физика");
        var rule = new ArchivistRule
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            Pattern = "конспект",
            MatchType = matchType,
            Enabled = true,
        };

        var result = service.Suggest("конспект_лекции.docx", [subject], [rule]);

        Assert.Empty(result);
    }

    [Fact]
    public void Case_is_normalized()
    {
        var service = Make(minScore: 0.1);
        var subject = MakeSubject("матан");

        var upper = service.Suggest("МАТАН.docx", [subject], []);
        var lower = service.Suggest("матан.docx", [subject], []);

        Assert.Equal(upper.Single().Score, lower.Single().Score, 6);
    }

    [Fact]
    public void Top_n_is_truncated_and_ordered_descending()
    {
        var service = Make(minScore: 0, maxCandidates: 2);
        var subjects = new[]
        {
            MakeSubject("матан лекция конспект"),  // 3 общих токена с файлом
            MakeSubject("матан лекция"),            // 2 общих токена
            MakeSubject("матан"),                   // 1 общий токен
        };

        var result = service.Suggest("матан_лекция_конспект.docx", subjects, []);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].Score >= result[1].Score);
        Assert.Equal(subjects[0].Id, result[0].SubjectId);
    }

    [Fact]
    public void Threshold_cuts_off_low_scores()
    {
        var subject = MakeSubject("матан семинар конспект вопросы");
        var lenient = Make(minScore: 0.1).Suggest("матан.docx", [subject], []);
        var strict = Make(minScore: 0.9).Suggest("матан.docx", [subject], []);

        Assert.NotEmpty(lenient);
        Assert.Empty(strict);
    }

    [Fact]
    public void Disabled_feature_returns_empty_even_on_perfect_match()
    {
        var service = Make(enabled: false);
        var subject = MakeSubject("матан");

        var result = service.Suggest("матан.docx", [subject], []);

        Assert.Empty(result);
    }

    [Fact]
    public void Equal_scores_are_ordered_deterministically_by_name()
    {
        var service = Make(minScore: 0.1);
        var subjects = new[] { MakeSubject("Бета матан"), MakeSubject("Альфа матан") };

        var result = service.Suggest("матан.docx", subjects, []);

        Assert.Equal(2, result.Count);
        Assert.Equal("Альфа матан", result[0].SubjectName);
        Assert.Equal("Бета матан", result[1].SubjectName);
    }
}
