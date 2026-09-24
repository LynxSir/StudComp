using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Зачётка и прогноз итоговой оценки (ARCHITECTURE §9.4): CRUD <see cref="GradeEntry"/> с валидацией
/// поверх <see cref="IGradeRepository"/> и расчёт прогноза выбранной для предмета стратегией.
/// Жёсткие ошибки — через <see cref="Result"/>; «мягкий» исход (недостижимая цель, низкая уверенность)
/// живёт внутри <see cref="ForecastResult"/>.
/// </summary>
public interface IGradeBookService
{
    /// <summary>Все строки зачётки по предмету, по возрастанию даты (и оценённые, и запланированные).</summary>
    Task<IReadOnlyList<GradeEntry>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(GradeEntry entry, CancellationToken ct = default);

    Task<Result> UpdateAsync(GradeEntry entry, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Прогноз итога по предмету стратегией, выбранной в настройках предмета (<see cref="Subject.ForecastStrategyName"/>).
    /// <paramref name="targetFinalGrade"/> задан — попутно решается обратная задача «сколько нужно на ближайшей аттестации».
    /// </summary>
    Task<Result<ForecastResult>> ForecastAsync(Guid subjectId, decimal? targetFinalGrade = null, CancellationToken ct = default);

    /// <summary>
    /// Прогноз конкретной стратегией (<paramref name="strategyName"/> — ключ keyed-сервиса, пусто/неизвестно →
    /// стратегия по умолчанию) в обход настройки предмета. Нужен UI зачётки, чтобы показать разницу стратегий
    /// рядом (DoD Phase 11).
    /// </summary>
    Task<Result<ForecastResult>> ForecastWithAsync(
        Guid subjectId, string strategyName, decimal? targetFinalGrade = null, CancellationToken ct = default);
}

internal sealed class GradeBookService(
    IGradeRepository grades,
    ISubjectRepository subjects,
    IGradeForecastStrategyResolver strategies,
    ILogger<GradeBookService> logger) : IGradeBookService
{
    public Task<IReadOnlyList<GradeEntry>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default) =>
        grades.GetBySubjectAsync(subjectId, ct);

    public async Task<Result<Guid>> CreateAsync(GradeEntry entry, CancellationToken ct = default)
    {
        Guard.NotNull(entry);

        var validation = await ValidateAsync(entry, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return Result<Guid>.Failure(validation.Error);
        }

        entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
        await grades.AddAsync(entry, ct).ConfigureAwait(false);
        return Result<Guid>.Success(entry.Id);
    }

    public async Task<Result> UpdateAsync(GradeEntry entry, CancellationToken ct = default)
    {
        Guard.NotNull(entry);

        var validation = await ValidateAsync(entry, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (await grades.GetByIdAsync(entry.Id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.grade_not_found", "Оценка не найдена.");
        }

        await grades.UpdateAsync(entry, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await grades.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.grade_not_found", "Оценка не найдена.");
        }

        await grades.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<Result<ForecastResult>> ForecastAsync(
        Guid subjectId,
        decimal? targetFinalGrade = null,
        CancellationToken ct = default) =>
        ForecastCoreAsync(subjectId, strategyName: null, targetFinalGrade, ct);

    public Task<Result<ForecastResult>> ForecastWithAsync(
        Guid subjectId,
        string strategyName,
        decimal? targetFinalGrade = null,
        CancellationToken ct = default) =>
        ForecastCoreAsync(subjectId, strategyName, targetFinalGrade, ct);

    /// <summary>
    /// Общее тело прогноза. <paramref name="strategyName"/> = <see langword="null"/> — берём стратегию из
    /// настроек предмета; иначе резолвим переданный ключ (пусто/неизвестно → стратегия по умолчанию).
    /// </summary>
    private async Task<Result<ForecastResult>> ForecastCoreAsync(
        Guid subjectId,
        string? strategyName,
        decimal? targetFinalGrade,
        CancellationToken ct)
    {
        var subject = await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return Result<ForecastResult>.Failure("organizer.subject_not_found", "Предмет не найден.");
        }

        var all = await grades.GetBySubjectAsync(subjectId, ct).ConfigureAwait(false);

        var graded = all.Where(e => !e.IsPlanned).ToList();
        var pending = all
            .Where(e => e.IsPlanned)
            .OrderBy(e => e.Date)
            .Select(e => new PendingAssessment(
                string.IsNullOrWhiteSpace(e.Title) ? TypeName(e.Type) : e.Title,
                e.MaxScore,
                e.Weight,
                e.Date))
            .ToList();

        var history = new SubjectGradeHistory(subjectId, graded, pending);
        var scale = ScaleFor(subject);
        var strategy = strategies.Resolve(strategyName ?? subject.ForecastStrategyName);

        try
        {
            return strategy.Forecast(history, scale, targetFinalGrade);
        }
        catch (Exception ex)
        {
            // Прогноз — чистая арифметика, но воркер/VM не должны падать из-за одной кривой строки (§11.3).
            logger.LogError(ex, "Не удалось посчитать прогноз по предмету {SubjectId}", subjectId);
            return Result<ForecastResult>.Failure("organizer.forecast_failed", "Не удалось посчитать прогноз.");
        }
    }

    /// <summary>Шкала оценивания предмета; для произвольной — границы предмета с ориентиром 100/60.</summary>
    private static GradeScale ScaleFor(Subject subject) => subject.GradeScaleKind switch
    {
        GradeScaleKind.Custom => new GradeScale(
            0m,
            subject.GradeScaleMax ?? 100m,
            subject.GradeScalePassThreshold ?? 60m,
            GradeScaleKind.Custom),
        var kind => GradeScale.For(kind),
    };

    private async Task<Result> ValidateAsync(GradeEntry entry, CancellationToken ct)
    {
        if (entry.MaxScore <= 0m)
        {
            return Result.Failure("organizer.grade_maxscore_invalid", "Максимальный балл должен быть больше нуля.");
        }

        if (entry.Weight < 0m)
        {
            return Result.Failure("organizer.grade_weight_invalid", "Вес не может быть отрицательным.");
        }

        if (!entry.IsPlanned && (entry.RawScore < 0m || entry.RawScore > entry.MaxScore))
        {
            return Result.Failure("organizer.grade_rawscore_invalid", "Набранный балл должен быть в пределах от нуля до максимального.");
        }

        if (await subjects.GetByIdAsync(entry.SubjectId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("organizer.subject_not_found", "Предмет не найден.");
        }

        return Result.Success();
    }

    private static string TypeName(GradeEntryType type) => type switch
    {
        GradeEntryType.Exam => "Экзамен",
        GradeEntryType.Test => "Зачёт / контрольная",
        GradeEntryType.CourseWork => "Курсовая",
        GradeEntryType.Attestation => "Аттестация",
        _ => "Аттестация",
    };
}
