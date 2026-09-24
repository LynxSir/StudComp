using StudComp.Core.Domain;

namespace StudComp.Modules.Organizer.Tests;

public sealed class SubjectServiceTests : OrganizerDatabaseTestBase
{
    [Theory]
    [InlineData(SubjectAssessment.Credit)]
    [InlineData(SubjectAssessment.GradedCredit)]
    [InlineData(SubjectAssessment.Exam)]
    [InlineData(SubjectAssessment.CourseWork)]
    [InlineData(SubjectAssessment.IndependentWork)]
    public async Task Assessment_is_persisted_on_creation_and_edit(SubjectAssessment assessment)
    {
        var id = (await Subjects.CreateAsync(new Subject { Name = "Предмет", Assessment = assessment })).Value;
        var subject = (await Subjects.GetByIdAsync(id))!;
        Assert.Equal(assessment, subject.Assessment);
        subject.Assessment = SubjectAssessment.Unspecified;
        Assert.True((await Subjects.UpdateAsync(subject)).IsSuccess);
        Assert.Equal(SubjectAssessment.Unspecified, (await Subjects.GetByIdAsync(id))!.Assessment);
    }

    [Fact]
    public async Task Create_read_update_delete_round_trips()
    {
        var created = await Subjects.CreateAsync(new Subject { Name = "Физика", Code = "Ф" });
        Assert.True(created.IsSuccess);

        var all = await Subjects.GetAllAsync();
        Assert.Contains(all, s => s.Id == created.Value && s.Name == "Физика");

        var update = await Subjects.UpdateAsync(new Subject { Id = created.Value, Name = "Физика-2", Code = "Ф" });
        Assert.True(update.IsSuccess);
        Assert.Equal("Физика-2", (await Subjects.GetAllAsync()).Single(s => s.Id == created.Value).Name);

        var delete = await Subjects.DeleteAsync(created.Value);
        Assert.True(delete.IsSuccess);
        Assert.Empty(await Subjects.GetAllAsync());
    }

    [Fact]
    public async Task Create_with_blank_name_fails()
    {
        var result = await Subjects.CreateAsync(new Subject { Name = "   " });

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_name_required", result.Error.Code);
    }

    [Fact]
    public async Task Update_missing_subject_fails()
    {
        var result = await Subjects.UpdateAsync(new Subject { Id = Guid.NewGuid(), Name = "Нет такого" });

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_not_found", result.Error.Code);
    }
}
