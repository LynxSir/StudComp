using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>
/// Доступ к журналу ответов (new_addons.md §3.1). Только он даёт статистику раздела и разбор ошибок.
/// </summary>
/// <remarks>
/// Журнал растёт неограниченно — строка на каждый ответ, тысячи за семестр. Поэтому всё, что
/// считается «по срезу», считается запросом: выгружать историю в память ради одного числа нельзя.
/// Единое определение «ответ верный» — <c>WasCorrect = 1</c> либо самооценка не ниже
/// <see cref="ReviewGrade.Good"/> — продублировано во всех агрегатах и вынесено в
/// <see cref="CorrectExpression"/>.
/// </remarks>
public interface ICardReviewLogRepository
{
    /// <summary>Запись ответа. Пишется сразу после оценки, а не в конце сессии.</summary>
    Task AddAsync(CardReviewLog entry, CancellationToken ct = default);

    /// <summary>История ответов по карточке, свежие сверху.</summary>
    Task<IReadOnlyList<CardReviewLog>> GetByCardAsync(
        Guid cardId,
        int take,
        CancellationToken ct = default);

    /// <summary>Все ответы одной сессии в порядке их появления — разбор ошибок и итоги.</summary>
    Task<IReadOnlyList<CardReviewLog>> GetBySessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Ответы за период — сырьё для статистики и календаря активности.</summary>
    Task<IReadOnlyList<CardReviewLog>> GetSinceAsync(DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Сколько ответов и сколько верных по дням — календарь активности и серия дней (§6.5).
    /// </summary>
    /// <param name="offsetMinutes">Сдвиг «UTC → учебные сутки», см. <see cref="SqliteDayGrouping"/>.</param>
    Task<IReadOnlyList<DailyReviewCount>> GetDailyCountsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int offsetMinutes,
        CancellationToken ct = default);

    /// <summary>Точность по предметам за период. Предмет живёт на карточке, поэтому это join.</summary>
    Task<IReadOnlyList<ReviewAccuracyRow>> GetAccuracyBySubjectAsync(
        DateTimeOffset since,
        CancellationToken ct = default);

    /// <summary>Точность по меткам за период, худшие сверху.</summary>
    Task<IReadOnlyList<ReviewAccuracyRow>> GetAccuracyByTagAsync(
        DateTimeOffset since,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// «Слабые места»: карточки с худшим отношением верных к общему (§6.5). Сортировка по доле с
    /// обрезкой физически невозможна в памяти — чтобы взять двадцать худших, надо посчитать все.
    /// </summary>
    Task<IReadOnlyList<ReviewAccuracyRow>> GetWeakCardsAsync(
        DateTimeOffset since,
        int minAnswers,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Промахи — основа «работы над ошибками». Либо по конкретной сессии, либо за период; удалённые
    /// и отложенные карточки отсеиваются здесь же, чтобы наверх не всплыло то, чего уже нет.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetMissedCardIdsAsync(
        DateTimeOffset since,
        Guid? sessionId,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Сколько новых карточек уже введено с указанного момента — дневной лимит новых.
    /// </summary>
    /// <remarks>
    /// «Новая» опознаётся по <c>IntervalBeforeDays = 0</c>: у по-настоящему новой карточки интервал
    /// нулевой, а у провалившейся шаг переучивания даёт около 0.007 дня. Признак однозначен и не
    /// требует отдельной колонки в журнале.
    /// </remarks>
    Task<int> CountNewIntroducedSinceAsync(DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Сколько повторений сделано с момента — дневной лимит повторений.</summary>
    Task<int> CountReviewsSinceAsync(DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Прогресс аврала по предмету: сколько показов сделано и когда начали (§6.4).</summary>
    Task<CramProgressRow> GetCramProgressAsync(
        Guid subjectId,
        DateTimeOffset notBefore,
        CancellationToken ct = default);
}

internal sealed class CardReviewLogRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), ICardReviewLogRepository
{
    /// <summary>
    /// Единое определение «ответ верный»: автопроверка сказала «да», либо при самооценке пользователь
    /// поставил себе не ниже «Хорошо».
    /// </summary>
    private const string CorrectExpression = "(WasCorrect = 1 OR (WasCorrect IS NULL AND Grade >= 2))";

    public async Task AddAsync(CardReviewLog entry, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.CardReviewLogs.Add(entry);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CardReviewLog>> GetByCardAsync(
        Guid cardId,
        int take,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardReviewLogs
            .AsNoTracking()
            .Where(x => x.CardId == cardId)
            .OrderByDescending(x => x.ReviewedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CardReviewLog>> GetBySessionAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardReviewLogs
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.ReviewedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CardReviewLog>> GetSinceAsync(
        DateTimeOffset since,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardReviewLogs
            .AsNoTracking()
            .Where(x => x.ReviewedAt >= since)
            .OrderBy(x => x.ReviewedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DailyReviewCount>> GetDailyCountsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int offsetMinutes,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        return await SqliteDayGrouping
            .ReadAsync(
                context,
                $"""
                 SELECT date(ReviewedAt, $shift) AS Day,
                        COUNT(*) AS Answers,
                        SUM(CASE WHEN {CorrectExpression} THEN 1 ELSE 0 END) AS Correct
                   FROM CardReviewLogs
                  WHERE ReviewedAt >= $from AND ReviewedAt < $to
                  GROUP BY 1
                  ORDER BY 1
                 """,
                command =>
                {
                    SqliteDayGrouping.AddShift(command, offsetMinutes);
                    SqliteDayGrouping.AddInstant(command, "$from", from);
                    SqliteDayGrouping.AddInstant(command, "$to", to);
                },
                reader => new DailyReviewCount(
                    SqliteDayGrouping.ReadDay(reader, 0),
                    reader.GetInt32(1),
                    reader.GetInt32(2)),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReviewAccuracyRow>> GetAccuracyBySubjectAsync(
        DateTimeOffset since,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        return await SqliteDayGrouping
            .ReadAsync(
                context,
                $"""
                 SELECT c.SubjectId AS Id,
                        COUNT(*) AS Answers,
                        SUM(CASE WHEN {CorrectExpression} THEN 1 ELSE 0 END) AS Correct
                   FROM CardReviewLogs l
                   JOIN Cards c ON c.Id = l.CardId
                  WHERE l.ReviewedAt >= $since
                  GROUP BY c.SubjectId
                  ORDER BY Answers DESC
                 """,
                command => SqliteDayGrouping.AddInstant(command, "$since", since),
                ReadAccuracy,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReviewAccuracyRow>> GetAccuracyByTagAsync(
        DateTimeOffset since,
        int take,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        return await SqliteDayGrouping
            .ReadAsync(
                context,
                $"""
                 SELECT t.TagId AS Id,
                        COUNT(*) AS Answers,
                        SUM(CASE WHEN {CorrectExpression} THEN 1 ELSE 0 END) AS Correct
                   FROM CardReviewLogs l
                   JOIN CardTagLinks t ON t.CardId = l.CardId
                  WHERE l.ReviewedAt >= $since
                  GROUP BY t.TagId
                  ORDER BY (CAST(Correct AS REAL) / Answers) ASC, Answers DESC
                  LIMIT $take
                 """,
                command =>
                {
                    SqliteDayGrouping.AddInstant(command, "$since", since);
                    command.Parameters.Add(new SqliteParameter("$take", Math.Max(1, take)));
                },
                ReadAccuracy,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReviewAccuracyRow>> GetWeakCardsAsync(
        DateTimeOffset since,
        int minAnswers,
        int take,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        return await SqliteDayGrouping
            .ReadAsync(
                context,
                $"""
                 SELECT l.CardId AS Id,
                        COUNT(*) AS Answers,
                        SUM(CASE WHEN {CorrectExpression} THEN 1 ELSE 0 END) AS Correct
                   FROM CardReviewLogs l
                   JOIN Cards c ON c.Id = l.CardId
                  WHERE l.ReviewedAt >= $since AND c.DeletedAt IS NULL
                  GROUP BY l.CardId
                 HAVING COUNT(*) >= $minAnswers
                  ORDER BY (CAST(Correct AS REAL) / Answers) ASC, Answers DESC
                  LIMIT $take
                 """,
                command =>
                {
                    SqliteDayGrouping.AddInstant(command, "$since", since);
                    command.Parameters.Add(new SqliteParameter("$minAnswers", Math.Max(1, minAnswers)));
                    command.Parameters.Add(new SqliteParameter("$take", Math.Max(1, take)));
                },
                ReadAccuracy,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> GetMissedCardIdsAsync(
        DateTimeOffset since,
        Guid? sessionId,
        int take,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var query = context.CardReviewLogs
            .AsNoTracking()
            .Where(x => x.ReviewedAt >= since)
            .Where(x => x.WasCorrect == false || (x.WasCorrect == null && x.Grade < ReviewGrade.Good));

        if (sessionId is { } id)
        {
            query = query.Where(x => x.SessionId == id);
        }

        // Отсев удалённых и отложенных: поднимать в «работу над ошибками» то, чего уже нет, нельзя.
        var alive = query.Where(x =>
            context.Cards.Any(c => c.Id == x.CardId && c.DeletedAt == null && !c.IsSuspended));

        return await alive
            .GroupBy(x => x.CardId)
            .OrderByDescending(g => g.Max(x => x.ReviewedAt))
            .Select(g => g.Key)
            .Take(Math.Max(1, take))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> CountNewIntroducedSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardReviewLogs
            .AsNoTracking()
            .Where(x => x.ReviewedAt >= since && x.IntervalBeforeDays == 0)
            .Select(x => x.CardId)
            .Distinct()
            .CountAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> CountReviewsSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.CardReviewLogs
            .AsNoTracking()
            .CountAsync(x => x.ReviewedAt >= since, ct)
            .ConfigureAwait(false);
    }

    public async Task<CramProgressRow> GetCramProgressAsync(
        Guid subjectId,
        DateTimeOffset notBefore,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var rows = await context.CardReviewLogs
            .AsNoTracking()
            .Where(x => x.ReviewedAt >= notBefore && x.Mode == StudyMode.Cram)
            .Where(x => context.Cards.Any(c => c.Id == x.CardId && c.SubjectId == subjectId))
            .Select(x => x.ReviewedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new CramProgressRow(rows.Count, rows.Count == 0 ? null : rows.Min());
    }

    private static ReviewAccuracyRow ReadAccuracy(System.Data.Common.DbDataReader reader) =>
        new(
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.GetInt32(1),
            reader.GetInt32(2));
}
