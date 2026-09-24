using Microsoft.Extensions.Options;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>Один предмет-кандидат для файла из «Неразобранного» с оценкой похожести в [0, 1].</summary>
public sealed record UnsortedSuggestion(Guid SubjectId, string SubjectName, double Score);

/// <summary>
/// Подсказки предметов-кандидатов для «Неразобранного» (ARCHITECTURE §8.4 п.7, new_addons.md §4):
/// лёгкая эвристика по токенам имени файла, без ML — тот же приём, что у <c>DeadlineMatching</c>
/// (Organizer, Phase 10), и второй потребитель <see cref="TokenSimilarity"/>.
/// </summary>
/// <remarks>
/// Чистая функция без диска/БД: вызывается пачкой на весь список «Неразобранного» (потенциально
/// тысячи раз за одно обновление), поэтому данные (<see cref="Subject"/>, <see cref="ArchivistRule"/>)
/// приходят параметрами — вызывающая сторона и так уже держит их в памяти.
/// </remarks>
public interface IUnsortedSuggestionService
{
    IReadOnlyList<UnsortedSuggestion> Suggest(
        string fileName,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<ArchivistRule> rules);
}

/// <inheritdoc cref="IUnsortedSuggestionService"/>
internal sealed class UnsortedSuggestionService(IOptionsMonitor<ArchivistOptions> options)
    : IUnsortedSuggestionService
{
    public IReadOnlyList<UnsortedSuggestion> Suggest(
        string fileName,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<ArchivistRule> rules)
    {
        var opt = options.CurrentValue;
        if (!opt.UnsortedSuggestionsEnabled || subjects.Count == 0 || string.IsNullOrWhiteSpace(fileName))
        {
            return [];
        }

        var fileTokens = TokenSimilarity.Tokenize(Path.GetFileNameWithoutExtension(fileName));
        if (fileTokens.Count == 0)
        {
            return [];
        }

        return subjects
            .Select(s => (Subject: s, Score: TokenSimilarity.Dice(fileTokens, SubjectTokens(s, rules))))
            .Where(x => x.Score >= opt.UnsortedSuggestionMinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Subject.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Max(0, opt.UnsortedSuggestionMaxCandidates))
            .Select(x => new UnsortedSuggestion(x.Subject.Id, x.Subject.Name, x.Score))
            .ToList();
    }

    /// <summary>
    /// Токены предмета: название + код + ключевые слова его включённых Keyword-правил — правило
    /// «конспект → Матан» уже несёт готовую подсказку, глупо её игнорировать. Extension/Regex-правила
    /// не участвуют: токен из расширения (напр. «docx») совпал бы почти с любым файлом и обесценил бы
    /// эвристику.
    /// </summary>
    private static HashSet<string> SubjectTokens(Subject subject, IReadOnlyList<ArchivistRule> rules)
    {
        var tokens = TokenSimilarity.Tokenize(subject.Name);
        tokens.UnionWith(TokenSimilarity.Tokenize(subject.Code));

        foreach (var rule in rules)
        {
            if (rule.Enabled && rule.MatchType == RuleMatchType.Keyword && rule.SubjectId == subject.Id)
            {
                tokens.UnionWith(TokenSimilarity.Tokenize(rule.Pattern));
            }
        }

        return tokens;
    }
}
