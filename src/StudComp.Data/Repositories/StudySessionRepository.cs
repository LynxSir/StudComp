using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>
/// Доступ к сессиям работы с карточками (new_addons.md §3.1). Как и журнал ответов, наполняется в
/// фазе тренажёра — здесь минимум под уже существующую таблицу.
/// </summary>
public interface IStudySessionRepository
{
    /// <summary>Завести сессию. Вызывается <b>при старте</b>, чтобы прерванная не пропала бесследно.</summary>
    Task AddAsync(StudySession session, CancellationToken ct = default);

    Task UpdateAsync(StudySession session, CancellationToken ct = default);

    Task<StudySession?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Последние сессии — вкладка «Статистика».</summary>
    Task<IReadOnlyList<StudySession>> GetRecentAsync(int take, CancellationToken ct = default);

    /// <summary>Последние сессии заданного режима — «пройти тот же экзамен ещё раз».</summary>
    Task<IReadOnlyList<StudySession>> GetRecentByModeAsync(
        StudyMode mode,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Незаконченная сессия не старше указанного возраста — предложение «продолжить».
    /// </summary>
    Task<StudySession?> GetLastUnfinishedAsync(TimeSpan maxAge, CancellationToken ct = default);

    /// <summary>
    /// Сдвинуть счётчики сессии на каждый ответ.
    /// </summary>
    /// <remarks>
    /// Именно инкремент, а не запись всей строки: чтение-правка-запись на каждый ответ рискует
    /// затереть <c>FinishedAt</c>, выставленный таймером сессии в соседней задаче.
    /// </remarks>
    Task<int> IncrementCountersAsync(
        Guid sessionId,
        int answeredDelta,
        int correctDelta,
        CancellationToken ct = default);

    /// <summary>Закрыть сессию. Повторный вызов ничего не меняет.</summary>
    Task<int> FinishAsync(Guid sessionId, DateTimeOffset finishedAt, CancellationToken ct = default);
}

internal sealed class StudySessionRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IStudySessionRepository
{
    public async Task AddAsync(StudySession session, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.StudySessions.Add(session);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(StudySession session, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.StudySessions.Update(session);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<StudySession?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.StudySessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StudySession>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.StudySessions
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StudySession>> GetRecentByModeAsync(
        StudyMode mode,
        int take,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.StudySessions
            .AsNoTracking()
            .Where(x => x.Mode == mode)
            .OrderByDescending(x => x.StartedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<StudySession?> GetLastUnfinishedAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var notBefore = DateTimeOffset.Now - maxAge;

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.StudySessions
            .AsNoTracking()
            .Where(x => x.FinishedAt == null && x.StartedAt >= notBefore)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> IncrementCountersAsync(
        Guid sessionId,
        int answeredDelta,
        int correctDelta,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.StudySessions
            .Where(x => x.Id == sessionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.AnsweredCount, x => x.AnsweredCount + answeredDelta)
                    .SetProperty(x => x.CorrectCount, x => x.CorrectCount + correctDelta),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<int> FinishAsync(
        Guid sessionId,
        DateTimeOffset finishedAt,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.StudySessions
            .Where(x => x.Id == sessionId && x.FinishedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.FinishedAt, (DateTimeOffset?)finishedAt),
                ct)
            .ConfigureAwait(false);
    }
}
