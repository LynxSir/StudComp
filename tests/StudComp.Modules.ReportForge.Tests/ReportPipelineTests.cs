using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Пайплайн целиком на реальной temp-базе: файл на диске, запись <c>ReportJob</c>, ветки ошибок
/// (ARCHITECTURE §10.3).
/// </summary>
public class ReportPipelineTests : ReportForgeDatabaseTestBase
{
    private const string SampleMarkdown = "# Введение\n\nТекст отчёта.";

    [Fact]
    public async Task Successful_run_writes_the_file_and_records_a_rendered_job()
    {
        using var folder = new TempFolder("rf-pipeline");
        var output = folder.Combine("отчёт.docx");

        var result = await Pipeline.RunAsync(Request(output, markdown: SampleMarkdown), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(output, result.OutputPath);
        Assert.True(File.Exists(output));

        var job = Assert.Single(await JobRepo.GetRecentAsync(10));
        Assert.Equal(ReportJobStatus.Rendered, job.Status);
        Assert.Equal(output, job.OutputPath);
    }

    [Fact]
    public async Task Generated_file_opens_as_a_valid_package()
    {
        using var folder = new TempFolder("rf-pipeline-valid");
        var output = folder.Combine("отчёт.docx");

        await Pipeline.RunAsync(Request(output, markdown: SampleMarkdown), CancellationToken.None);

        var errors = DocxTestHelpers.Validate(await File.ReadAllBytesAsync(output));
        Assert.True(errors.Count == 0, DocxTestHelpers.Describe(errors));
    }

    [Fact]
    public async Task Missing_output_directory_is_created()
    {
        using var folder = new TempFolder("rf-pipeline-mkdir");
        var output = folder.Combine("вложенная", "папка", "отчёт.docx");

        var result = await Pipeline.RunAsync(Request(output, markdown: SampleMarkdown), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task Markdown_file_takes_priority_over_typed_text()
    {
        using var folder = new TempFolder("rf-pipeline-source");
        var source = folder.WriteFile("исходник.md", "# Из файла");
        var output = folder.Combine("отчёт.docx");

        var request = new ReportJobRequest(
            TemplateId: null,
            SubjectId: null,
            MarkdownSource: "# Из текстового поля",
            SourcePath: source,
            OutputPath: output,
            TitlePage: null,
            GenerateTableOfContents: false);

        var result = await Pipeline.RunAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Contains("Из файла", DocxTestHelpers.ReadText(await File.ReadAllBytesAsync(output)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Relative_image_path_is_resolved_against_the_markdown_file()
    {
        using var folder = new TempFolder("rf-pipeline-image");
        TestImages.Write(folder.Combine("images", "pipeline.png"));
        var source = folder.WriteFile("исходник.md", "![Схема](images/pipeline.png)");
        var output = folder.Combine("отчёт.docx");

        var result = await Pipeline.RunAsync(
            Request(output, sourcePath: source),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.Message);

        var docx = await File.ReadAllBytesAsync(output);
        Assert.Single(DocxTestHelpers.ReadDocument(docx).Descendants(DocxTestHelpers.W + "drawing"));
        Assert.Contains("Рисунок 1 — Схема", DocxTestHelpers.ReadParagraphs(docx));
    }

    [Fact]
    public async Task Empty_source_is_rejected_before_anything_is_written()
    {
        using var folder = new TempFolder("rf-pipeline-empty");
        var output = folder.Combine("отчёт.docx");

        var result = await Pipeline.RunAsync(Request(output, markdown: "   "), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("reportforge.empty_source", result.Error?.Code);
        Assert.False(File.Exists(output));
        Assert.Empty(await JobRepo.GetRecentAsync(10));
    }

    [Fact]
    public async Task Missing_markdown_file_is_reported_as_such()
    {
        using var folder = new TempFolder("rf-pipeline-missing");

        var result = await Pipeline.RunAsync(
            Request(folder.Combine("отчёт.docx"), sourcePath: folder.Combine("нет.md")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("reportforge.source_missing", result.Error?.Code);
    }

    [Fact]
    public async Task Output_path_is_required()
    {
        var result = await Pipeline.RunAsync(Request(string.Empty, markdown: SampleMarkdown), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("reportforge.output_path_required", result.Error?.Code);
    }

    [Fact]
    public async Task Locked_output_file_fails_with_a_clear_code_and_a_failed_job()
    {
        using var folder = new TempFolder("rf-pipeline-locked");
        var output = folder.WriteFile("отчёт.docx", "занято");

        using (File.Open(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await Pipeline.RunAsync(Request(output, markdown: SampleMarkdown), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("reportforge.output_locked", result.Error?.Code);

            var job = Assert.Single(await JobRepo.GetRecentAsync(10));
            Assert.Equal(ReportJobStatus.Failed, job.Status);
        }

        // Чужой файл остался на месте и нетронутым — то же требование, что и к Архивариусу (§14).
        Assert.Equal("занято", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task Default_template_is_seeded_once_no_matter_how_many_reports_are_built()
    {
        using var folder = new TempFolder("rf-pipeline-template");

        await Pipeline.RunAsync(Request(folder.Combine("1.docx"), markdown: SampleMarkdown), CancellationToken.None);
        await Pipeline.RunAsync(Request(folder.Combine("2.docx"), markdown: SampleMarkdown), CancellationToken.None);

        Assert.Equal(1, await CountTemplatesAsync());

        var template = Assert.Single(await TemplateRepo.GetAllAsync());
        Assert.Equal("7.32-2017", template.GostVariant);
        Assert.All(await JobRepo.GetRecentAsync(10), job => Assert.Equal(template.Id, job.TemplateId));
    }

    [Fact]
    public async Task Template_service_returns_the_same_template_on_repeated_calls()
    {
        var first = await Templates.EnsureDefaultTemplateAsync();
        var second = await Templates.EnsureDefaultTemplateAsync();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await CountTemplatesAsync());
    }

    [Fact]
    public async Task Profile_of_the_seeded_template_matches_the_embedded_one()
    {
        var template = await Templates.EnsureDefaultTemplateAsync();
        var profile = await Profiles.GetForTemplateAsync(template.Id);

        DocxTestHelpers.AssertSameProfile(DocxTestHelpers.DefaultProfile, profile);
    }

    [Fact]
    public async Task Broken_style_json_falls_back_to_the_factory_profile()
    {
        var template = await Templates.EnsureDefaultTemplateAsync();
        template.StyleProfileJson = "{ это не json";
        await TemplateRepo.UpdateAsync(template);

        var profile = await Profiles.GetForTemplateAsync(template.Id);

        DocxTestHelpers.AssertSameProfile(DocxTestHelpers.DefaultProfile, profile);
    }

    [Fact]
    public async Task Cloned_profile_with_a_ten_millimetre_right_margin_reaches_the_generated_document()
    {
        // Сценарий DoD Phase 9: клонировать заводской профиль, поменять правое поле на 10 мм, сгенерировать.
        var clone = await Templates.CloneAsync(null, "Методичка кафедры");
        var edited = (await Templates.GetProfileAsync(clone.Id)) with { Margins = new(30d, 10d, 20d, 20d) };
        await Templates.SaveAsync(clone.Id, "Методичка кафедры", edited);

        using var folder = new TempFolder("rf-pipeline-profile");
        var output = folder.Combine("отчёт.docx");
        var request = new ReportJobRequest(
            TemplateId: clone.Id,
            SubjectId: null,
            MarkdownSource: SampleMarkdown,
            SourcePath: null,
            OutputPath: output,
            TitlePage: null,
            GenerateTableOfContents: false);

        var result = await Pipeline.RunAsync(request, CancellationToken.None);
        Assert.True(result.Succeeded, result.Error?.Message);

        var docx = await File.ReadAllBytesAsync(output);
        Assert.Empty(DocxTestHelpers.Validate(docx));

        var margin = DocxTestHelpers.ReadDocument(docx)
            .Descendants(DocxTestHelpers.W + "pgMar")
            .Single();
        Assert.Equal("567", margin.Attribute(DocxTestHelpers.W + "right")!.Value);   // 10 мм
        Assert.Equal("1701", margin.Attribute(DocxTestHelpers.W + "left")!.Value);   // 30 мм осталось
    }

    /// <summary>
    /// DoD Phase 12.9 (new_addons.md §7.5, §12): «Собрать из карточек» → макет «Шпаргалка» — тот же
    /// приём клонирования профиля, что уже проверен для методички кафедры, только компактнее.
    /// <c>CardComposeLayoutViewModel</c> живёт в <c>StudComp.App</c> (тестов там нет), поэтому здесь
    /// проверяется сам механизм: клонированный компактный профиль честно доезжает до документа.
    /// </summary>
    [Fact]
    public async Task Cheat_sheet_profile_produces_a_valid_compact_document()
    {
        var clone = await Templates.CloneAsync(null, "Шпаргалка");
        var compact = (await Templates.GetProfileAsync(clone.Id)) with
        {
            FontSizePt = 10,
            LineSpacing = 1.0,
            ParagraphIndentCm = 0,
            Margins = new MarginsMm(10, 10, 10, 10),
        };
        await Templates.SaveAsync(clone.Id, "Шпаргалка", compact);

        const string cheatSheetMarkdown =
            "**Интеграл** — предел интегральных сумм.\n\n**Производная** — скорость изменения функции.\n";

        using var folder = new TempFolder("rf-pipeline-cheatsheet");
        var output = folder.Combine("шпаргалка.docx");
        var request = new ReportJobRequest(
            TemplateId: clone.Id,
            SubjectId: null,
            MarkdownSource: cheatSheetMarkdown,
            SourcePath: null,
            OutputPath: output,
            TitlePage: null,
            GenerateTableOfContents: false);

        var result = await Pipeline.RunAsync(request, CancellationToken.None);
        Assert.True(result.Succeeded, result.Error?.Message);

        var docx = await File.ReadAllBytesAsync(output);
        Assert.Empty(DocxTestHelpers.Validate(docx));

        var margin = DocxTestHelpers.ReadDocument(docx).Descendants(DocxTestHelpers.W + "pgMar").Single();
        Assert.Equal("567", margin.Attribute(DocxTestHelpers.W + "right")!.Value); // 10 мм по всем сторонам
        Assert.Equal("567", margin.Attribute(DocxTestHelpers.W + "left")!.Value);
    }

    private static ReportJobRequest Request(
        string outputPath,
        string? markdown = null,
        string? sourcePath = null) => new(
        TemplateId: null,
        SubjectId: null,
        MarkdownSource: markdown,
        SourcePath: sourcePath,
        OutputPath: outputPath,
        TitlePage: null,
        GenerateTableOfContents: false);
}
