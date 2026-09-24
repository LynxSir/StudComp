using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Операции над расписанием (ARCHITECTURE §9.2): валидация времени и привязки к предмету поверх
/// <see cref="IScheduleRepository"/>.
/// </summary>
public interface IScheduleService
{
    Task<IReadOnlyList<ScheduleEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Пары для недели заданной чётности. <paramref name="parity"/> = <see langword="null"/> или
    /// <see cref="WeekParity.Any"/> — вернуть всё. Иначе — пары с <see cref="WeekParity.Any"/> плюс
    /// пары этой чётности.
    /// </summary>
    Task<IReadOnlyList<ScheduleEntry>> GetForWeekAsync(WeekParity? parity, CancellationToken ct = default);

    /// <summary>Пары одного предмета — Хаб предмета и расчёт часов за семестр.</summary>
    Task<IReadOnlyList<ScheduleEntry>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(ScheduleEntry entry, CancellationToken ct = default);

    Task<Result> UpdateAsync(ScheduleEntry entry, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class ScheduleService(IScheduleRepository schedule, ISubjectRepository subjects) : IScheduleService
{
    public Task<IReadOnlyList<ScheduleEntry>> GetAllAsync(CancellationToken ct = default) =>
        schedule.GetAllAsync(ct);

    public async Task<IReadOnlyList<ScheduleEntry>> GetForWeekAsync(WeekParity? parity, CancellationToken ct = default)
    {
        var all = await schedule.GetAllAsync(ct).ConfigureAwait(false);
        if (parity is null or WeekParity.Any)
        {
            return all;
        }

        return all.Where(x => x.WeekParity == WeekParity.Any || x.WeekParity == parity).ToList();
    }

    public Task<IReadOnlyList<ScheduleEntry>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default) =>
        schedule.GetBySubjectAsync(subjectId, ct);

    public async Task<Result<Guid>> CreateAsync(ScheduleEntry entry, CancellationToken ct = default)
    {
        Guard.NotNull(entry);

        var validation = await ValidateAsync(entry, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return Result<Guid>.Failure(validation.Error);
        }

        entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
        await schedule.AddAsync(entry, ct).ConfigureAwait(false);
        return Result<Guid>.Success(entry.Id);
    }

    public async Task<Result> UpdateAsync(ScheduleEntry entry, CancellationToken ct = default)
    {
        Guard.NotNull(entry);

        var validation = await ValidateAsync(entry, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (await schedule.GetByIdAsync(entry.Id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.schedule_not_found", "Пара не найдена.");
        }

        await schedule.UpdateAsync(entry, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await schedule.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.schedule_not_found", "Пара не найдена.");
        }

        await schedule.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> ValidateAsync(ScheduleEntry entry, CancellationToken ct)
    {
        if (entry.EndTime <= entry.StartTime)
        {
            return Result.Failure("organizer.schedule_time_invalid", "Время окончания должно быть позже начала.");
        }

        if (await subjects.GetByIdAsync(entry.SubjectId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.subject_not_found", "Предмет не найден.");
        }

        return Result.Success();
    }
}
