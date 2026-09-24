using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Операции над учебными семестрами (new_addons.md §5). Семестр — источник дат для чётности недели
/// и расчёта часов; до Phase 12.3 эти данные жили двумя настройками в <c>usersettings.json</c>.
/// </summary>
/// <remarks>
/// Сервис — singleton с кешем активного семестра: его читают планировщик напоминаний, провайдер
/// активного предмета (тик раз в минуту), Дашборд и расписание. Ходить за ним в БД на каждый тик
/// незачем, поэтому кеш обновляется только на изменениях и по явному <see cref="RefreshAsync"/>,
/// а слушатели узнают о смене через <see cref="ActiveChanged"/>.
/// </remarks>
public interface ISemesterService
{
    /// <summary>Активный семестр из кеша; <see langword="null"/> — кеш ещё не прогрет или семестров нет.</summary>
    Semester? Current { get; }

    /// <summary>Срабатывает, когда активный семестр сменился или изменились его даты.</summary>
    event EventHandler? ActiveChanged;

    Task<IReadOnlyList<Semester>> GetAllAsync(CancellationToken ct = default);

    Task<Semester?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Активный семестр, при необходимости прогревая кеш.</summary>
    Task<Semester?> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Перечитать кеш из БД (после внешних изменений).</summary>
    Task RefreshAsync(CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(Semester semester, CancellationToken ct = default);

    Task<Result> UpdateAsync(Semester semester, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<Result> SetActiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Разовый перенос старых настроек <c>Rubrica:Organizer</c> в сущность. Идемпотентен: если хоть
    /// один семестр уже есть — не делает ничего. При отсутствии даты начала семестр не создаётся:
    /// придумывать за пользователя учебный календарь мы не вправе.
    /// </summary>
    Task<Result> SeedFromLegacyAsync(DateOnly? startDate, bool firstWeekIsOdd, CancellationToken ct = default);
}

internal sealed class SemesterService(ISemesterRepository semesters) : ISemesterService
{
    private const int MaxCourseNumber = 12;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;

    public Semester? Current { get; private set; }

    public event EventHandler? ActiveChanged;

    public Task<IReadOnlyList<Semester>> GetAllAsync(CancellationToken ct = default) =>
        semesters.GetAllAsync(ct);

    public Task<Semester?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        semesters.GetByIdAsync(id, ct);

    public async Task<Semester?> GetActiveAsync(CancellationToken ct = default)
    {
        if (!_loaded)
        {
            await RefreshAsync(ct).ConfigureAwait(false);
        }

        return Current;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var active = await semesters.GetActiveAsync(ct).ConfigureAwait(false);

            // Активного нет, но семестры есть — берём самый свежий, чтобы приложение не выглядело
            // «без семестра» из-за того, что флаг где-то потерялся.
            if (active is null)
            {
                var all = await semesters.GetAllAsync(ct).ConfigureAwait(false);
                active = all.FirstOrDefault();
            }

            var changed = !SameSemester(Current, active);
            Current = active;
            _loaded = true;

            if (changed)
            {
                ActiveChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Guid>> CreateAsync(Semester semester, CancellationToken ct = default)
    {
        Guard.NotNull(semester);

        var validation = Validate(semester);
        if (validation.IsFailure)
        {
            return Result<Guid>.Failure(validation.Error);
        }

        semester.Id = semester.Id == Guid.Empty ? Guid.NewGuid() : semester.Id;
        semester.PairSlotsJson = NormalizeSlots(semester.PairSlotsJson);

        // Первый заведённый семестр становится активным сам — иначе приложение осталось бы
        // «без семестра» сразу после того, как пользователь его создал.
        var isFirst = await semesters.CountAsync(ct).ConfigureAwait(false) == 0;
        semester.IsActive = semester.IsActive || isFirst;

        await semesters.AddAsync(semester, ct).ConfigureAwait(false);

        if (semester.IsActive)
        {
            await semesters.SetActiveAsync(semester.Id, ct).ConfigureAwait(false);
        }

        await RefreshAsync(ct).ConfigureAwait(false);
        return Result<Guid>.Success(semester.Id);
    }

    public async Task<Result> UpdateAsync(Semester semester, CancellationToken ct = default)
    {
        Guard.NotNull(semester);

        var validation = Validate(semester);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (await semesters.GetByIdAsync(semester.Id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.semester_not_found", "Семестр не найден.");
        }

        semester.PairSlotsJson = NormalizeSlots(semester.PairSlotsJson);
        await semesters.UpdateAsync(semester, ct).ConfigureAwait(false);

        var wasCurrent = Current?.Id == semester.Id;
        await RefreshAsync(ct).ConfigureAwait(false);

        // Правка дат активного семестра меняет чётность недели и расчёт часов — будим слушателей,
        // даже если RefreshAsync не увидел смены самого семестра.
        if (wasCurrent)
        {
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await semesters.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.semester_not_found", "Семестр не найден.");
        }

        await semesters.DeleteAsync(id, ct).ConfigureAwait(false);
        await RefreshAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(Guid id, CancellationToken ct = default)
    {
        if (await semesters.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.semester_not_found", "Семестр не найден.");
        }

        await semesters.SetActiveAsync(id, ct).ConfigureAwait(false);
        await RefreshAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SeedFromLegacyAsync(
        DateOnly? startDate, bool firstWeekIsOdd, CancellationToken ct = default)
    {
        if (await semesters.CountAsync(ct).ConfigureAwait(false) > 0)
        {
            return Result.Success();
        }

        if (startDate is not { } start)
        {
            // Дата начала не задавалась — переносить нечего, семестр заведёт сам пользователь.
            return Result.Success();
        }

        var semester = new Semester
        {
            Id = Guid.NewGuid(),
            Name = "Текущий семестр",
            CourseNumber = 1,
            StartDate = start,
            EndDate = null,
            FirstWeekIsOdd = firstWeekIsOdd,
            IsActive = true,
            PairSlotsJson = "[]",
        };

        await semesters.AddAsync(semester, ct).ConfigureAwait(false);
        await RefreshAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static Result Validate(Semester semester)
    {
        if (string.IsNullOrWhiteSpace(semester.Name))
        {
            return Result.Failure("organizer.semester_name_required", "Название семестра обязательно.");
        }

        if (semester.CourseNumber is < 1 or > MaxCourseNumber)
        {
            return Result.Failure(
                "organizer.semester_course_invalid", $"Курс должен быть от 1 до {MaxCourseNumber}.");
        }

        if (semester.EndDate is { } end && end <= semester.StartDate)
        {
            return Result.Failure(
                "organizer.semester_dates_invalid", "Дата окончания должна быть позже даты начала.");
        }

        if (semester.DefaultBreakMinutes < 0)
        {
            return Result.Failure(
                "organizer.semester_break_invalid", "Перерыв между парами не может быть отрицательным.");
        }

        if (semester.LunchBreakStart is null != semester.LunchBreakEnd is null)
        {
            return Result.Failure(
                "organizer.semester_lunch_invalid", "Укажите оба времени обеденного перерыва или ни одного.");
        }

        if (semester.LunchBreakStart is { } lunchStart && semester.LunchBreakEnd is { } lunchEnd
            && lunchEnd <= lunchStart)
        {
            return Result.Failure(
                "organizer.semester_lunch_invalid",
                "Обеденный перерыв должен заканчиваться позже, чем начинается.");
        }

        return Result.Success();
    }

    /// <summary>Прогоняем сетку звонков через разбор — в БД не попадёт битый или неотсортированный JSON.</summary>
    private static string NormalizeSlots(string? json) => PairSlots.Serialize(PairSlots.Parse(json));

    private static bool SameSemester(Semester? left, Semester? right)
    {
        if (left is null || right is null)
        {
            return ReferenceEquals(left, right);
        }

        return left.Id == right.Id
            && left.StartDate == right.StartDate
            && left.EndDate == right.EndDate
            && left.FirstWeekIsOdd == right.FirstWeekIsOdd;
    }
}
