using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Операции над дедлайнами (ARCHITECTURE §9.3): валидация + <see cref="Result"/> поверх
/// <see cref="IDeadlineRepository"/>. Статус <c>Overdue</c> не хранится — он производный, его выводит
/// вызывающий (VM/запрос).
/// </summary>
public interface IDeadlineService
{
    Task<IReadOnlyList<Deadline>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Deadline>> GetUpcomingAsync(TimeSpan window, CancellationToken ct = default);

    /// <summary>Все дедлайны предмета — нужны подбору кандидата на привязку файла (ARCHITECTURE §9.3).</summary>
    Task<IReadOnlyList<Deadline>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<Deadline?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(Deadline deadline, CancellationToken ct = default);

    Task<Result> UpdateAsync(Deadline deadline, CancellationToken ct = default);

    Task<Result> SetStatusAsync(Guid id, DeadlineStatus status, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Привязать к дедлайну файл, разложенный архивариусом (ARCHITECTURE §9.3). Вызывается по кнопке
    /// «Привязать» в тосте-предложении.
    /// </summary>
    Task<Result> LinkFileAsync(Guid deadlineId, Guid fileRecordId, CancellationToken ct = default);

    /// <summary>Снять привязку файла к дедлайну.</summary>
    Task<Result> UnlinkFileAsync(Guid deadlineId, CancellationToken ct = default);

    /// <summary>
    /// Путь привязанного файла — для строки «скрепка» в карточке дедлайна. <see langword="null"/>,
    /// если привязки нет или запись учёта исчезла.
    /// </summary>
    Task<string?> GetLinkedFilePathAsync(Guid deadlineId, CancellationToken ct = default);
}

internal sealed class DeadlineService(
    IDeadlineRepository deadlines,
    ISubjectRepository subjects,
    IFileRecordRepository fileRecords) : IDeadlineService
{
    public Task<IReadOnlyList<Deadline>> GetAllAsync(CancellationToken ct = default) =>
        deadlines.GetAllAsync(ct);

    public Task<Deadline?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        deadlines.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Deadline>> GetUpcomingAsync(TimeSpan window, CancellationToken ct = default) =>
        deadlines.GetUpcomingAsync(window, ct);

    public Task<IReadOnlyList<Deadline>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default) =>
        deadlines.GetBySubjectAsync(subjectId, ct);

    public async Task<Result<Guid>> CreateAsync(Deadline deadline, CancellationToken ct = default)
    {
        Guard.NotNull(deadline);

        var validation = await ValidateAsync(deadline, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return Result<Guid>.Failure(validation.Error);
        }

        deadline.Id = deadline.Id == Guid.Empty ? Guid.NewGuid() : deadline.Id;
        await deadlines.AddAsync(deadline, ct).ConfigureAwait(false);
        return Result<Guid>.Success(deadline.Id);
    }

    public async Task<Result> UpdateAsync(Deadline deadline, CancellationToken ct = default)
    {
        Guard.NotNull(deadline);

        var validation = await ValidateAsync(deadline, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (await deadlines.GetByIdAsync(deadline.Id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
        }

        await deadlines.UpdateAsync(deadline, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetStatusAsync(Guid id, DeadlineStatus status, CancellationToken ct = default)
    {
        var deadline = await deadlines.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (deadline is null)
        {
            return Result.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
        }

        deadline.Status = status;
        await deadlines.UpdateAsync(deadline, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await deadlines.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
        }

        await deadlines.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> LinkFileAsync(Guid deadlineId, Guid fileRecordId, CancellationToken ct = default)
    {
        var deadline = await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
        if (deadline is null)
        {
            return Result.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
        }

        if (await fileRecords.GetByIdAsync(fileRecordId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.file_record_not_found", "Запись о файле не найдена.");
        }

        deadline.LinkedFileRecordId = fileRecordId;
        await deadlines.UpdateAsync(deadline, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> UnlinkFileAsync(Guid deadlineId, CancellationToken ct = default)
    {
        var deadline = await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
        if (deadline is null)
        {
            return Result.Failure("organizer.deadline_not_found", "Дедлайн не найден.");
        }

        deadline.LinkedFileRecordId = null;
        await deadlines.UpdateAsync(deadline, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<string?> GetLinkedFilePathAsync(Guid deadlineId, CancellationToken ct = default)
    {
        var deadline = await deadlines.GetByIdAsync(deadlineId, ct).ConfigureAwait(false);
        if (deadline?.LinkedFileRecordId is not { } fileRecordId)
        {
            return null;
        }

        var record = await fileRecords.GetByIdAsync(fileRecordId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        return record.CurrentPath.Length > 0 ? record.CurrentPath : record.OriginalPath;
    }

    private async Task<Result> ValidateAsync(Deadline deadline, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deadline.Title))
        {
            return Result.Failure("organizer.deadline_title_required", "Название дедлайна обязательно.");
        }

        if (await subjects.GetByIdAsync(deadline.SubjectId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.subject_not_found", "Предмет не найден.");
        }

        return Result.Success();
    }
}
