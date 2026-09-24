using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Domain;
using StudComp.Modules.Organizer.Services.ForecastStrategies;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Интеграционные тесты зачётки поверх реального temp-SQLite: CRUD с кодами ошибок и прогноз,
/// уважающий шкалу предмета (ARCHITECTURE §9.4, §12 — «фикстуры разных GradeScale»).
/// </summary>
public sealed class GradeBookServiceTests : OrganizerDatabaseTestBase
{
    [Fact]
    public async Task Create_then_read_round_trips_the_entry()
    {
        var subjectId = await SeedSubjectAsync();

        var created = await Grades.CreateAsync(new GradeEntry
        {
            SubjectId = subjectId,
            Type = GradeEntryType.Exam,
            Title = "Экзамен",
            RawScore = 4m,
            MaxScore = 5m,
            Weight = 1m,
            Date = new DateTime(2026, 1, 20),

        });

        Assert.True(created.IsSuccess);
        var stored = Assert.Single(await Grades.GetBySubjectAsync(subjectId));
        Assert.Equal("Экзамен", stored.Title);
        Assert.Equal(4m, stored.RawScore);
    }

    [Fact]
    public async Task Create_rejects_a_non_positive_maximum()
    {
        var subjectId = await SeedSubjectAsync();

        var result = await Grades.CreateAsync(new GradeEntry { SubjectId = subjectId, MaxScore = 0m, Weight = 1m });

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.grade_maxscore_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Create_rejects_a_score_above_the_maximum_for_a_graded_entry()
    {
        var subjectId = await SeedSubjectAsync();

        var result = await Grades.CreateAsync(new GradeEntry
        {
            SubjectId = subjectId,
            RawScore = 12m,
            MaxScore = 10m,
            Weight = 1m,
        });

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.grade_rawscore_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Planned_assessment_needs_no_score()
    {
        var subjectId = await SeedSubjectAsync();

        var result = await Grades.CreateAsync(new GradeEntry
        {
            SubjectId = subjectId,
            Title = "Лабораторная 5",
            IsPlanned = true,
            RawScore = 0m,
            MaxScore = 100m,
            Weight = 1m,
        });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_subject()
    {
        var result = await Grades.CreateAsync(new GradeEntry
        {
            SubjectId = Guid.NewGuid(),
            RawScore = 1m,
            MaxScore = 5m,
            Weight = 1m,
        });

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Update_and_delete_report_not_found_for_a_missing_entry()
    {
        var subjectId = await SeedSubjectAsync();

        var update = await Grades.UpdateAsync(new GradeEntry
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            RawScore = 1m,
            MaxScore = 5m,
            Weight = 1m,
        });
        var delete = await Grades.DeleteAsync(Guid.NewGuid());

        Assert.Equal("organizer.grade_not_found", update.Error.Code);
        Assert.Equal("organizer.grade_not_found", delete.Error.Code);
    }

    [Fact]
    public async Task Update_then_delete_work_on_an_existing_entry()
    {
        var subjectId = await SeedSubjectAsync();
        var id = await SeedGradeAsync(subjectId, raw: 3m, max: 5m, weight: 1m);

        var update = await Grades.UpdateAsync(new GradeEntry
        {
            Id = id,
            SubjectId = subjectId,
            Type = GradeEntryType.Attestation,
            Title = "Правка",
            RawScore = 5m,
            MaxScore = 5m,
            Weight = 2m,
            Date = DateTime.Today,

        });
        Assert.True(update.IsSuccess);
        Assert.Equal(5m, (await Grades.GetBySubjectAsync(subjectId)).Single().RawScore);

        var delete = await Grades.DeleteAsync(id);
        Assert.True(delete.IsSuccess);
        Assert.Empty(await Grades.GetBySubjectAsync(subjectId));
    }

    [Fact]
    public async Task Forecast_fails_for_an_unknown_subject()
    {
        var result = await Grades.ForecastAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Forecast_uses_graded_and_planned_rows_on_the_default_five_point_scale()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedGradeAsync(subjectId, raw: 80m, max: 100m, weight: 1m);
        await SeedGradeAsync(subjectId, raw: 60m, max: 100m, weight: 1m);
        await SeedGradeAsync(subjectId, raw: 0m, max: 100m, weight: 1m, planned: true);

        var result = await Grades.ForecastAsync(subjectId);

        Assert.True(result.IsSuccess);
        Assert.Equal(4.1m, result.Value.PredictedFinalScore, 2);
        Assert.Equal("weighted-average", result.Value.StrategyUsed);
    }

    [Fact]
    public async Task Forecast_respects_a_hundred_point_subject_scale()
    {
        var id = (await Subjects.CreateAsync(new Subject
        {
            Name = "БЖД",
            Code = "БЖ",
            GradeScaleKind = GradeScaleKind.HundredPoint,
        })).Value;
        await SeedGradeAsync(id, raw: 80m, max: 100m, weight: 1m);
        await SeedGradeAsync(id, raw: 60m, max: 100m, weight: 1m);

        var result = await Grades.ForecastAsync(id);

        Assert.True(result.IsSuccess);
        Assert.Equal(70m, result.Value.PredictedFinalScore, 2);
    }

    [Fact]
    public async Task Forecast_respects_a_custom_subject_scale()
    {
        var id = (await Subjects.CreateAsync(new Subject
        {
            Name = "Проект",
            Code = "ПР",
            GradeScaleKind = GradeScaleKind.Custom,
            GradeScaleMax = 12m,
            GradeScalePassThreshold = 4m,
        })).Value;
        await SeedGradeAsync(id, raw: 50m, max: 100m, weight: 1m);

        var result = await Grades.ForecastAsync(id);

        Assert.True(result.IsSuccess);
        Assert.InRange(result.Value.PredictedFinalScore, 0m, 12m);
    }

    [Fact]
    public async Task Forecast_falls_back_when_the_subject_names_an_unknown_strategy()
    {
        var id = (await Subjects.CreateAsync(new Subject
        {
            Name = "История",
            Code = "ИС",
            ForecastStrategyName = "does-not-exist",
        })).Value;
        await SeedGradeAsync(id, raw: 4m, max: 5m, weight: 1m);

        var result = await Grades.ForecastAsync(id);

        Assert.True(result.IsSuccess);
        Assert.Equal("weighted-average", result.Value.StrategyUsed);
    }

    [Fact]
    public async Task Forecast_uses_the_linear_regression_strategy_chosen_for_the_subject()
    {
        var origin = new DateTime(2026, 3, 1);
        var id = (await Subjects.CreateAsync(new Subject
        {
            Name = "Матан",
            Code = "МА",
            ForecastStrategyName = "linear-regression",
        })).Value;

        // Растущая история: 0,5 → 0,9 с равным шагом по времени.
        for (var i = 0; i < 5; i++)
        {
            await SeedGradeAsync(id, raw: 50m + (i * 10m), max: 100m, weight: 1m, date: origin.AddDays(i * 10));
        }

        await SeedGradeAsync(id, raw: 0m, max: 100m, weight: 1m, planned: true, date: origin.AddDays(50));

        var linear = await Grades.ForecastAsync(id);
        var weighted = await Grades.ForecastWithAsync(id, WeightedAverageForecastStrategy.Key);

        Assert.True(linear.IsSuccess);
        Assert.Equal("linear-regression", linear.Value.StrategyUsed);
        Assert.Equal("weighted-average", weighted.Value.StrategyUsed);
        Assert.True(
            linear.Value.PredictedFinalScore > weighted.Value.PredictedFinalScore,
            $"linear {linear.Value.PredictedFinalScore} должен быть выше weighted {weighted.Value.PredictedFinalScore}");
    }

    [Fact]
    public async Task ForecastWith_overrides_the_subjects_default_strategy()
    {
        var subjectId = await SeedSubjectAsync(); // ForecastStrategyName пуст → средневзвешенная
        await SeedGradeAsync(subjectId, raw: 60m, max: 100m, weight: 1m);
        await SeedGradeAsync(subjectId, raw: 80m, max: 100m, weight: 1m);

        var byDefault = await Grades.ForecastAsync(subjectId);
        var overridden = await Grades.ForecastWithAsync(subjectId, "linear-regression");

        Assert.Equal("weighted-average", byDefault.Value.StrategyUsed);
        Assert.Equal("linear-regression", overridden.Value.StrategyUsed);
    }

    [Fact]
    public async Task ForecastWith_falls_back_to_the_default_for_an_unknown_strategy_key()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedGradeAsync(subjectId, raw: 4m, max: 5m, weight: 1m);

        var result = await Grades.ForecastWithAsync(subjectId, "nonsense");

        Assert.True(result.IsSuccess);
        Assert.Equal("weighted-average", result.Value.StrategyUsed);
    }

    [Fact]
    public async Task ForecastWith_fails_for_an_unknown_subject()
    {
        var result = await Grades.ForecastWithAsync(Guid.NewGuid(), "linear-regression");

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_not_found", result.Error.Code);
    }
}
