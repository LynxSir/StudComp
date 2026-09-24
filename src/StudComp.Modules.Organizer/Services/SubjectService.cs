using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Операции над предметами (ARCHITECTURE §9.1): валидация + оборачивание в <see cref="Result"/>
/// поверх <see cref="ISubjectRepository"/>. Предмет — узел интеграции трёх модулей, но CRUD его
/// живёт в Органайзере (PLAN.md Phase 4).
/// </summary>
public interface ISubjectService
{
    Task<IReadOnlyList<Subject>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Предмет по идентификатору — нужен Хабу предмета (new_addons.md §5).</summary>
    Task<Subject?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(Subject subject, CancellationToken ct = default);

    Task<Result> UpdateAsync(Subject subject, CancellationToken ct = default);

    /// <summary>Удаляет предмет; расписание, дедлайны и оценки уходят каскадом (ARCHITECTURE §7.2).</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}

internal sealed class SubjectService(ISubjectRepository subjects) : ISubjectService
{
    public Task<IReadOnlyList<Subject>> GetAllAsync(CancellationToken ct = default) =>
        subjects.GetAllAsync(ct);

    public Task<Subject?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        subjects.GetByIdAsync(id, ct);

    public async Task<Result<Guid>> CreateAsync(Subject subject, CancellationToken ct = default)
    {
        Guard.NotNull(subject);

        if (string.IsNullOrWhiteSpace(subject.Name))
        {
            return Result<Guid>.Failure("organizer.subject_name_required", "Название предмета обязательно.");
        }

        subject.Id = subject.Id == Guid.Empty ? Guid.NewGuid() : subject.Id;
        await subjects.AddAsync(subject, ct).ConfigureAwait(false);
        return Result<Guid>.Success(subject.Id);
    }

    public async Task<Result> UpdateAsync(Subject subject, CancellationToken ct = default)
    {
        Guard.NotNull(subject);

        if (string.IsNullOrWhiteSpace(subject.Name))
        {
            return Result.Failure("organizer.subject_name_required", "Название предмета обязательно.");
        }

        if (await subjects.GetByIdAsync(subject.Id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.subject_not_found", "Предмет не найден.");
        }

        await subjects.UpdateAsync(subject, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await subjects.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.subject_not_found", "Предмет не найден.");
        }

        await subjects.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }
}
