using StudComp.Core.Domain;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Операции редактора профилей: клонирование, сохранение формы, удаление с защитами (Phase 9).
/// Всё на реальной temp-базе через <see cref="ReportForgeDatabaseTestBase"/>.
/// </summary>
public class ReportTemplateServiceTests : ReportForgeDatabaseTestBase
{
    [Fact]
    public async Task Cloning_the_default_creates_a_separate_custom_template()
    {
        var source = await Templates.EnsureDefaultTemplateAsync();

        var clone = await Templates.CloneAsync(source.Id, "Кафедральный профиль");

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("Кафедральный профиль", clone.Name);
        Assert.Equal("custom", clone.GostVariant);
        Assert.Equal(source.StyleProfileJson, clone.StyleProfileJson);
        Assert.Equal(2, await CountTemplatesAsync());
    }

    [Fact]
    public async Task Cloning_a_null_source_uses_the_embedded_profile()
    {
        var clone = await Templates.CloneAsync(null, "С нуля");

        var profile = await Templates.GetProfileAsync(clone.Id);
        DocxTestHelpers.AssertSameProfile(DocxTestHelpers.DefaultProfile, profile);
    }

    [Fact]
    public async Task Saving_the_form_persists_the_edited_margin()
    {
        var clone = await Templates.CloneAsync(null, "10 мм справа");
        var edited = (await Templates.GetProfileAsync(clone.Id)) with { Margins = new(30d, 10d, 20d, 20d) };

        await Templates.SaveAsync(clone.Id, "Правое поле 10", edited);

        var reloaded = await Templates.GetProfileAsync(clone.Id);
        Assert.Equal(10d, reloaded.Margins.Right);
        Assert.Equal(30d, reloaded.Margins.Left);

        var template = await TemplateRepo.GetByIdAsync(clone.Id);
        Assert.Equal("Правое поле 10", template!.Name);
    }

    [Fact]
    public async Task The_factory_template_cannot_be_deleted()
    {
        var factory = await Templates.EnsureDefaultTemplateAsync();

        Assert.False(await Templates.DeleteAsync(factory.Id));
        Assert.NotNull(await TemplateRepo.GetByIdAsync(factory.Id));
    }

    [Fact]
    public async Task A_template_referenced_by_a_report_cannot_be_deleted()
    {
        var clone = await Templates.CloneAsync(null, "В работе");
        await JobRepo.AddAsync(new ReportJob
        {
            Id = Guid.NewGuid(),
            TemplateId = clone.Id,
            SourcePath = string.Empty,
            OutputPath = "c:\\отчёт.docx",
            CreatedAt = DateTimeOffset.Now,
            Status = ReportJobStatus.Rendered,
        });

        Assert.True(await Templates.IsInUseAsync(clone.Id));
        Assert.False(await Templates.DeleteAsync(clone.Id));
        Assert.NotNull(await TemplateRepo.GetByIdAsync(clone.Id));
    }

    [Fact]
    public async Task An_unused_custom_template_is_deleted()
    {
        var clone = await Templates.CloneAsync(null, "Черновик");

        Assert.True(await Templates.DeleteAsync(clone.Id));
        Assert.Null(await TemplateRepo.GetByIdAsync(clone.Id));
    }
}
