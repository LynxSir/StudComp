using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к заметкам (ARCHITECTURE §7.3, new_addons.md §1.9).</summary>
public interface INoteRepository
{
    /// <summary>
    /// Заметки предмета: закреплённые сверху, дальше по свежести правки. Служебные заметки
    /// дедлайнов (<c>DeadlineId</c> задан) в список не входят — у них своя страница.
    /// </summary>
    Task<IReadOnlyList<Note>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>Последние изменённые заметки — зона «Последние заметки» на Дашборде.</summary>
    Task<IReadOnlyList<Note>> GetRecentAsync(int take, CancellationToken ct = default);

    Task<Note?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Заметки, привязанные к конкретному файлу или папке.</summary>
    Task<IReadOnlyList<Note>> GetByLinkedPathAsync(string linkedPath, CancellationToken ct = default);

    /// <summary>Заметки, сделанные с конкретной пары.</summary>
    Task<IReadOnlyList<Note>> GetByScheduleEntryAsync(Guid scheduleEntryId, CancellationToken ct = default);

    /// <summary>Служебные заметки дедлайна (задание и ответ).</summary>
    Task<IReadOnlyList<Note>> GetByDeadlineAsync(Guid deadlineId, CancellationToken ct = default);

    Task AddAsync(Note note, CancellationToken ct = default);

    Task UpdateAsync(Note note, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class NoteRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), INoteRepository
{
    public async Task<IReadOnlyList<Note>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Notes
            .AsNoTracking()
            .Where(x => x.SubjectId == subjectId && x.DeadlineId == null)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Note>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Notes
            .AsNoTracking()
            .Where(x => x.DeadlineId == null)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Note?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Note>> GetByLinkedPathAsync(string linkedPath, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Notes
            .AsNoTracking()
            .Where(x => x.LinkedPath == linkedPath)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Note>> GetByScheduleEntryAsync(Guid scheduleEntryId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Notes
            .AsNoTracking()
            .Where(x => x.ScheduleEntryId == scheduleEntryId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Note>> GetByDeadlineAsync(Guid deadlineId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Notes
            .AsNoTracking()
            .Where(x => x.DeadlineId == deadlineId)
            .OrderBy(x => x.Kind)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(Note note, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Notes.Add(note);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Note note, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Notes.Update(note);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        await context.Notes
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
