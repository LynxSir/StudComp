using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к дедлайнам (ARCHITECTURE §7.3, §9.3).</summary>
public interface IDeadlineRepository
{
    /// <summary>
    /// Незакрытые дедлайны со сроком в пределах <paramref name="window"/> от текущего момента,
    /// по возрастанию срока. «Просрочен» вызывающий определяет сам — это производное состояние
    /// (ARCHITECTURE §9.3).
    /// </summary>
    Task<IReadOnlyList<Deadline>> GetUpcomingAsync(TimeSpan window, CancellationToken ct = default);

    /// <summary>Все дедлайны (включая закрытые), по возрастанию срока — для списка на странице «Дедлайны».</summary>
    Task<IReadOnlyList<Deadline>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Deadline>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<Deadline?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Deadline deadline, CancellationToken ct = default);

    Task UpdateAsync(Deadline deadline, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class DeadlineRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IDeadlineRepository
{
    public async Task<IReadOnlyList<Deadline>> GetUpcomingAsync(TimeSpan window, CancellationToken ct = default)
    {
        var until = DateTimeOffset.UtcNow + window;

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Deadlines
            .AsNoTracking()
            .Where(x => x.Status == DeadlineStatus.Pending && x.DueDate <= until)
            .OrderBy(x => x.DueDate)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Deadline>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Deadlines
            .AsNoTracking()
            .OrderBy(x => x.DueDate)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Deadline>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Deadlines
            .AsNoTracking()
            .Where(x => x.SubjectId == subjectId)
            .OrderBy(x => x.DueDate)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Deadline?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Deadlines
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(Deadline deadline, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Deadlines.Add(deadline);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Deadline deadline, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Deadlines.Update(deadline);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.Deadlines
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
