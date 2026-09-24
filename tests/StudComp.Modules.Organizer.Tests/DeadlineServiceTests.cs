using StudComp.Core.Domain;

namespace StudComp.Modules.Organizer.Tests;

public sealed class DeadlineServiceTests : OrganizerDatabaseTestBase
{
    private static Deadline Deadline(Guid subjectId, DateTimeOffset due, DeadlineStatus status = DeadlineStatus.Pending) => new()
    {
        SubjectId = subjectId,
        Title = "Лабораторная 1",
        DueDate = due,
        Type = DeadlineType.Homework,
        Priority = DeadlinePriority.Normal,
        Status = status,
    };

    [Fact]
    public async Task Create_update_delete_round_trips()
    {
        var subjectId = await SeedSubjectAsync();

        var created = await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(3)));
        Assert.True(created.IsSuccess);
        Assert.Single(await Deadlines.GetAllAsync());

        var edited = Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(4));
        edited.Id = created.Value;
        edited.Title = "Лабораторная 1 (перенос)";
        Assert.True((await Deadlines.UpdateAsync(edited)).IsSuccess);
        Assert.Equal("Лабораторная 1 (перенос)", (await Deadlines.GetAllAsync()).Single().Title);

        Assert.True((await Deadlines.DeleteAsync(created.Value)).IsSuccess);
        Assert.Empty(await Deadlines.GetAllAsync());
    }

    [Fact]
    public async Task SetStatus_marks_done()
    {
        var subjectId = await SeedSubjectAsync();
        var created = await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(2)));

        Assert.True((await Deadlines.SetStatusAsync(created.Value, DeadlineStatus.Done)).IsSuccess);

        Assert.Equal(DeadlineStatus.Done, (await Deadlines.GetAllAsync()).Single().Status);
    }

    [Fact]
    public async Task GetUpcoming_filters_window_and_excludes_done()
    {
        var subjectId = await SeedSubjectAsync();
        await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(2)));
        await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(40)));
        var done = await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(1)));
        await Deadlines.SetStatusAsync(done.Value, DeadlineStatus.Done);

        var upcoming = await Deadlines.GetUpcomingAsync(TimeSpan.FromDays(7));

        Assert.Single(upcoming);
    }

    [Fact]
    public async Task Create_with_unknown_subject_fails()
    {
        var result = await Deadlines.CreateAsync(Deadline(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1)));

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Create_with_blank_title_fails()
    {
        var subjectId = await SeedSubjectAsync();
        var deadline = Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(1));
        deadline.Title = "  ";

        var result = await Deadlines.CreateAsync(deadline);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.deadline_title_required", result.Error.Code);
    }

    // --- Phase 10: привязка файла, разложенного архивариусом (ARCHITECTURE §9.3) ---

    [Fact]
    public async Task Link_then_unlink_round_trips()
    {
        var subjectId = await SeedSubjectAsync();
        var deadlineId = (await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(3)))).Value;
        var fileRecordId = await SeedFileRecordAsync();

        Assert.True((await Deadlines.LinkFileAsync(deadlineId, fileRecordId)).IsSuccess);
        Assert.Equal(fileRecordId, (await DeadlineRepo.GetByIdAsync(deadlineId))!.LinkedFileRecordId);

        Assert.True((await Deadlines.UnlinkFileAsync(deadlineId)).IsSuccess);
        Assert.Null((await DeadlineRepo.GetByIdAsync(deadlineId))!.LinkedFileRecordId);
    }

    [Fact]
    public async Task Link_to_a_missing_deadline_fails()
    {
        var fileRecordId = await SeedFileRecordAsync();

        var result = await Deadlines.LinkFileAsync(Guid.NewGuid(), fileRecordId);

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.deadline_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Link_to_a_missing_file_record_fails()
    {
        var subjectId = await SeedSubjectAsync();
        var deadlineId = (await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(3)))).Value;

        var result = await Deadlines.LinkFileAsync(deadlineId, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.file_record_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Unlink_of_a_missing_deadline_fails()
    {
        var result = await Deadlines.UnlinkFileAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.deadline_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Linked_file_path_is_resolved_for_the_card()
    {
        var subjectId = await SeedSubjectAsync();
        var deadlineId = (await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(3)))).Value;
        var fileRecordId = await SeedFileRecordAsync(@"C:\Архив\Матан\ЛР4.docx");
        await Deadlines.LinkFileAsync(deadlineId, fileRecordId);

        Assert.Equal(@"C:\Архив\Матан\ЛР4.docx", await Deadlines.GetLinkedFilePathAsync(deadlineId));
    }

    [Fact]
    public async Task Linked_file_path_is_null_without_a_link()
    {
        var subjectId = await SeedSubjectAsync();
        var deadlineId = (await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(3)))).Value;

        Assert.Null(await Deadlines.GetLinkedFilePathAsync(deadlineId));
        Assert.Null(await Deadlines.GetLinkedFilePathAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_the_file_record_only_clears_the_link()
    {
        // FK настроен на SET NULL (§7.1): дедлайн переживает исчезновение записи о файле.
        var subjectId = await SeedSubjectAsync();
        var deadlineId = (await Deadlines.CreateAsync(Deadline(subjectId, DateTimeOffset.UtcNow.AddDays(3)))).Value;
        var fileRecordId = await SeedFileRecordAsync();
        await Deadlines.LinkFileAsync(deadlineId, fileRecordId);

        await using (var context = await Factory.CreateDbContextAsync())
        {
            context.FileRecords.Remove(await context.FileRecords.FindAsync(fileRecordId) ?? throw new InvalidOperationException());
            await context.SaveChangesAsync();
        }

        var deadline = await DeadlineRepo.GetByIdAsync(deadlineId);
        Assert.NotNull(deadline);
        Assert.Null(deadline.LinkedFileRecordId);
    }

    [Fact]
    public async Task Deadlines_are_listed_per_subject()
    {
        var first = await SeedSubjectAsync("Матан");
        var second = await SeedSubjectAsync("Физика");
        await Deadlines.CreateAsync(Deadline(first, DateTimeOffset.UtcNow.AddDays(1)));
        await Deadlines.CreateAsync(Deadline(first, DateTimeOffset.UtcNow.AddDays(2)));
        await Deadlines.CreateAsync(Deadline(second, DateTimeOffset.UtcNow.AddDays(3)));

        Assert.Equal(2, (await Deadlines.GetBySubjectAsync(first)).Count);
        Assert.Single(await Deadlines.GetBySubjectAsync(second));
    }
}
