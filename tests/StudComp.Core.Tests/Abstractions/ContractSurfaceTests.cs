using System.Reflection;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Common;
using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Abstractions;

/// <summary>
/// Фиксирует публичные контракты основных модулей.
/// Само существование типов доказывает компиляция; эти тесты не дают незаметно переименовать типы
/// или изменить их сигнатуры.
/// </summary>
public class ContractSurfaceTests
{
    // ---- ARCHITECTURE §8.3 — Archivist -------------------------------------------------------

    [Fact]
    public void SortingRuleEngine_evaluates_a_file_against_prioritised_rules()
    {
        AssertMethod(
            typeof(ISortingRuleEngine),
            "EvaluateAsync",
            typeof(Task<SortDecision>),
            typeof(WatchedFileInfo),
            typeof(IReadOnlyList<ArchivistRule>),
            typeof(CancellationToken));
    }

    [Fact]
    public void FileOperationExecutor_moves_and_undoes()
    {
        AssertMethod(
            typeof(IFileOperationExecutor),
            "MoveAndRenameAsync",
            typeof(Task<FileOperationResult>),
            typeof(SortDecision),
            typeof(CancellationToken));

        AssertMethod(
            typeof(IFileOperationExecutor),
            "UndoAsync",
            typeof(Task<bool>),
            typeof(Guid),
            typeof(CancellationToken));
    }

    [Fact]
    public void FileStabilityChecker_waits_for_a_stable_file()
    {
        AssertMethod(
            typeof(IFileStabilityChecker),
            "WaitUntilStableAsync",
            typeof(Task<bool>),
            typeof(string),
            typeof(TimeSpan),
            typeof(CancellationToken));
    }

    [Fact]
    public void SortDecision_carries_the_documented_members()
    {
        AssertProperties(
            typeof(SortDecision),
            ("SourceFileRecordId", typeof(Guid)),
            ("SubjectId", typeof(Guid?)),
            ("TargetDirectory", typeof(string)),
            ("NewFileName", typeof(string)),
            ("MatchedRule", typeof(ArchivistRule)),
            ("Conflict", typeof(ConflictResolution)));
    }

    /// <summary>
    /// Сообщение о разложенном файле (ARCHITECTURE §9.3): единственный контракт, который пересекает
    /// границу между Архивариусом и Органайзером, поэтому его форма зафиксирована здесь.
    /// </summary>
    [Fact]
    public void FileSortedMessage_carries_the_documented_members()
    {
        AssertProperties(
            typeof(FileSortedMessage),
            ("FileRecordId", typeof(Guid)),
            ("SubjectId", typeof(Guid?)),
            ("FinalPath", typeof(string)),
            ("FileName", typeof(string)),
            ("SortedAt", typeof(DateTimeOffset)));
    }

    [Fact]
    public void ConflictResolution_never_offers_overwriting_a_user_file()
    {
        // ARCHITECTURE §8.6, §14: самое строгое требование проекта.
        Assert.Equal(
            new[] { "None", "AppendSuffix", "SkipDuplicateHash" },
            Enum.GetNames<ConflictResolution>());
    }

    [Fact]
    public void FileOperationOutcome_has_no_destructive_member()
    {
        Assert.Equal(
            new[] { "Moved", "SkippedDuplicate", "Deferred", "Failed" },
            Enum.GetNames<FileOperationOutcome>());
    }

    [Fact]
    public void RuleMatchType_covers_the_three_documented_kinds()
    {
        Assert.Equal(
            new[] { "Extension", "Keyword", "Regex" },
            Enum.GetNames<RuleMatchType>());
    }

    [Fact]
    public void Core_declares_no_hosted_service_markers()
    {
        // ADR §16.11: они живут в Modules.Archivist, чтобы Core остался только на BCL.
        var hostedMarkers = CoreAssembly
            .GetExportedTypes()
            .Where(type => type.Name.Contains("HostedService", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(hostedMarkers);
    }

    // ---- ARCHITECTURE §9.4 — Organizer -------------------------------------------------------

    [Fact]
    public void GradeForecastStrategy_forecasts_from_history_and_scale()
    {
        AssertMethod(
            typeof(IGradeForecastStrategy),
            "Forecast",
            typeof(ForecastResult),
            typeof(SubjectGradeHistory),
            typeof(GradeScale),
            typeof(decimal?));

        Assert.NotNull(typeof(IGradeForecastStrategy).GetProperty("Name", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void TargetFinalGrade_is_optional()
    {
        var parameter = typeof(IGradeForecastStrategy)
            .GetMethod("Forecast")!
            .GetParameters()[2];

        Assert.True(parameter.IsOptional);
        Assert.Null(parameter.DefaultValue);
    }

    [Fact]
    public void ForecastResult_carries_the_documented_members()
    {
        AssertProperties(
            typeof(ForecastResult),
            ("PredictedFinalScore", typeof(decimal)),
            ("RequiredScoreOnNextAssessment", typeof(decimal?)),
            ("Confidence", typeof(ConfidenceLevel)),
            ("StrategyUsed", typeof(string)),
            ("IsAchievable", typeof(bool)));
    }

    [Fact]
    public void SubjectGradeHistory_pairs_graded_entries_with_pending_ones()
    {
        AssertProperties(
            typeof(SubjectGradeHistory),
            ("SubjectId", typeof(Guid)),
            ("Entries", typeof(IReadOnlyList<GradeEntry>)),
            ("Pending", typeof(IReadOnlyList<PendingAssessment>)));
    }

    [Fact]
    public void GradeScale_supports_more_than_the_five_point_system()
    {
        AssertProperties(
            typeof(GradeScale),
            ("Min", typeof(decimal)),
            ("Max", typeof(decimal)),
            ("PassThreshold", typeof(decimal)),
            ("Kind", typeof(GradeScaleKind)));

        Assert.Equal(
            new[] { "FivePoint", "HundredPoint", "Custom" },
            Enum.GetNames<GradeScaleKind>());
    }

    [Fact]
    public void GradeScale_exposes_a_factory_for_the_standard_scales()
    {
        // Единая точка правды «сколько такое 3 по 5-балльной» — задел Phase 7, ADR §16.40.
        var factory = typeof(GradeScale).GetMethod(
            "For",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(GradeScaleKind)]);

        Assert.NotNull(factory);
        Assert.Equal(typeof(GradeScale), factory!.ReturnType);
    }

    // ---- ARCHITECTURE §10.3 — ReportForge ----------------------------------------------------

    [Fact]
    public void MarkdownDocumentModelBuilder_builds_the_intermediate_model()
    {
        AssertMethod(
            typeof(IMarkdownDocumentModelBuilder),
            "Build",
            typeof(ReportDocumentModel),
            typeof(string));
    }

    [Fact]
    public void GostDocxRenderer_renders_a_model_with_a_profile_into_a_stream()
    {
        AssertMethod(
            typeof(IGostDocxRenderer),
            "RenderAsync",
            typeof(Task),
            typeof(ReportDocumentModel),
            typeof(GostStyleProfile),
            typeof(Stream),
            typeof(CancellationToken));
    }

    [Fact]
    public void ReportPipeline_runs_a_request_end_to_end()
    {
        AssertMethod(
            typeof(IReportPipeline),
            "RunAsync",
            typeof(Task<ReportJobResult>),
            typeof(ReportJobRequest),
            typeof(CancellationToken));
    }

    [Theory]
    [InlineData(typeof(HeadingBlock))]
    [InlineData(typeof(ParagraphBlock))]
    [InlineData(typeof(ListBlock))]
    [InlineData(typeof(TableBlock))]
    [InlineData(typeof(CodeBlock))]
    [InlineData(typeof(ImageBlock))]
    public void Every_documented_block_implements_the_marker(Type blockType)
    {
        Assert.True(
            typeof(IReportBlock).IsAssignableFrom(blockType),
            $"{blockType.Name} must implement {nameof(IReportBlock)}.");
    }

    [Fact]
    public void No_block_type_is_missing_from_the_documented_set()
    {
        var blocks = CoreAssembly
            .GetExportedTypes()
            .Where(type => typeof(IReportBlock).IsAssignableFrom(type) && type != typeof(IReportBlock))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "CodeBlock", "HeadingBlock", "ImageBlock", "ListBlock", "ParagraphBlock", "TableBlock" },
            blocks);
    }

    [Fact]
    public void TableBlock_carries_its_caption()
    {
        // Подпись «Таблица N — Название» — требование ГОСТ; номер ставит рендерер, название живёт здесь (ADR §16.31).
        AssertProperties(
            typeof(TableBlock),
            ("Headers", typeof(IReadOnlyList<string>)),
            ("Rows", typeof(IReadOnlyList<IReadOnlyList<string>>)),
            ("Caption", typeof(string)));
    }

    [Fact]
    public void ReportDocumentModel_carries_the_documented_members()
    {
        AssertProperties(
            typeof(ReportDocumentModel),
            ("TitlePage", typeof(TitlePageInfo)),
            ("Blocks", typeof(IReadOnlyList<IReportBlock>)),
            ("GenerateTableOfContents", typeof(bool)));
    }

    [Fact]
    public void GostStyleProfile_exposes_every_field_listed_in_the_document()
    {
        // ARCHITECTURE §10.4 перечисляет эти поля поимённо; сами значения — в gost-7.32-2017.json (Phase 6).
        AssertProperties(
            typeof(GostStyleProfile),
            ("FontFamily", typeof(string)),
            ("FontSizePt", typeof(double)),
            ("LineSpacing", typeof(double)),
            ("Margins", typeof(MarginsMm)),
            ("ParagraphIndentCm", typeof(double)),
            ("BodyAlignment", typeof(ParagraphAlignment)),
            ("BulletMarker", typeof(string)),
            ("HeadingRules", typeof(IReadOnlyList<HeadingStyleRule>)),
            ("PageNumbering", typeof(PageNumberingOptions)),
            ("TableOfContents", typeof(TocOptions)),
            ("TitlePageTemplate", typeof(string)),
            ("CodeBlock", typeof(CodeBlockStyleRule)));
    }

    [Fact]
    public void CodeBlockStyleRule_exposes_every_field_listed_in_the_document()
    {
        // ARCHITECTURE §10.4/§10.5: оформление листинга — данные профиля, а не константы рендерера (Phase 9).
        AssertProperties(
            typeof(CodeBlockStyleRule),
            ("MonospaceFontFamily", typeof(string)),
            ("RelativeFontSizePt", typeof(double)),
            ("Boxed", typeof(bool)),
            ("BackgroundHex", typeof(string)));
    }

    // ---- new_addons.md §1.1 / §2 — Workspace (Phase 12.1) ----------------------------------

    [Fact]
    public void StudyWorkspace_exposes_the_documented_surface()
    {
        // Контракт учебной папки читают три модуля — форма зафиксирована здесь (new_addons.md §1.1).
        Assert.NotNull(typeof(IStudyWorkspace).GetProperty("StudyRootPath", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(IStudyWorkspace).GetProperty("HasStudyRoot", BindingFlags.Public | BindingFlags.Instance));

        AssertMethod(typeof(IStudyWorkspace), "GetSubjectDirectory", typeof(string), typeof(Subject));
        AssertMethod(typeof(IStudyWorkspace), "EnsureSubjectScaffold", typeof(void), typeof(Subject));
        AssertMethod(
            typeof(IStudyWorkspace),
            "EnumerateSubjectFiles",
            typeof(IReadOnlyList<string>),
            typeof(Subject),
            typeof(string));
        AssertMethod(typeof(IStudyWorkspace), "ResolveRelative", typeof(string), typeof(string));
    }

    // ---- new_addons.md §6.2 — Cards ----------------------------------------------------------

    [Fact]
    public void ReviewScheduler_exposes_the_documented_surface()
    {
        // Keyed-стратегия повторений: сигнатуру читает и резолвер модуля, и экран сессии
        // (подписи интервалов на кнопках оценки), поэтому она пришивается здесь.
        Assert.NotNull(typeof(IReviewScheduler).GetProperty("Name", BindingFlags.Public | BindingFlags.Instance));

        AssertMethod(
            typeof(IReviewScheduler),
            "Next",
            typeof(ReviewOutcome),
            typeof(ReviewState),
            typeof(ReviewGrade),
            typeof(DateTimeOffset),
            typeof(ReviewTuning));

        AssertMethod(
            typeof(IReviewScheduler),
            "Preview",
            typeof(IReadOnlyList<ReviewPreview>),
            typeof(ReviewState),
            typeof(DateTimeOffset),
            typeof(ReviewTuning));
    }

    [Fact]
    public void Study_session_planner_keeps_its_seeded_signature()
    {
        // Зерно лежит в базе годами: смена сигнатуры «повторить ту же сессию» ломает молча.
        var build = typeof(StudySessionPlanner).GetMethod(
            "Build",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(IReadOnlyList<StudyCandidate>), typeof(StudyPlanOptions), typeof(int)]);

        Assert.NotNull(build);
        Assert.Equal(typeof(IReadOnlyList<Guid>), build!.ReturnType);
    }

    [Fact]
    public void ActivityEntry_carries_the_documented_members()
    {
        AssertProperties(
            typeof(ActivityEntry),
            ("Id", typeof(Guid)),
            ("Kind", typeof(ActivityKind)),
            ("Timestamp", typeof(DateTimeOffset)),
            ("SubjectId", typeof(Guid?)),
            ("Path", typeof(string)),
            ("RefId", typeof(Guid?)),
            ("Title", typeof(string)));

        Assert.Equal(
            new[]
            {
                "FileOpened", "FileSorted", "ReportGenerated", "NoteEdited", "ClassAttended",
                "FileImported", "CardCreated", "CardReviewed",
            },
            Enum.GetNames<ActivityKind>());
    }

    // ---- вспомогательное ---------------------------------------------------------------------

    private static Assembly CoreAssembly => typeof(Result).Assembly;

    private static void AssertMethod(Type declaringType, string name, Type returnType, params Type[] parameterTypes)
    {
        var method = declaringType.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, parameterTypes);

        Assert.True(
            method is not null,
            $"{declaringType.Name}.{name}({string.Join(", ", parameterTypes.Select(t => t.Name))}) is missing.");
        Assert.Equal(returnType, method!.ReturnType);
    }

    private static void AssertProperties(Type type, params (string Name, Type PropertyType)[] expected)
    {
        foreach (var (name, propertyType) in expected)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

            Assert.True(property is not null, $"{type.Name}.{name} is missing.");
            Assert.Equal(propertyType, property!.PropertyType);
        }
    }
}
