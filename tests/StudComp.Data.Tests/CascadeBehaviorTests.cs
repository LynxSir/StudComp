using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Tests;

/// <summary>
/// Проверяет поведение внешних ключей при удалении: каскады для «дочерних» сущностей предмета и
/// <c>SET NULL</c> там, где терять запись нельзя (ARCHITECTURE §7.2, §9.3, §14; PLAN.md Phase 2 DoD).
/// </summary>
public sealed class CascadeBehaviorTests : DatabaseTestBase
{
    [Fact]
    public async Task Deleting_subject_cascades_to_schedule_and_deadlines_and_grades()
    {
        var subject = TestData.Subject();

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.ScheduleEntries.Add(TestData.ScheduleEntry(subject.Id));
            arrange.Deadlines.Add(TestData.Deadline(subject.Id, DateTimeOffset.UtcNow.AddDays(3)));
            arrange.GradeEntries.Add(TestData.GradeEntry(subject.Id, DateTime.UtcNow));
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Subjects.Where(x => x.Id == subject.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        Assert.Empty(assert.ScheduleEntries);
        Assert.Empty(assert.Deadlines);
        Assert.Empty(assert.GradeEntries);
    }

    [Fact]
    public async Task Deleting_file_record_nulls_deadline_link_and_keeps_deadline()
    {
        var subject = TestData.Subject();
        var fileRecord = TestData.FileRecord("hash-1", subject.Id, FileRecordStatus.Sorted);
        var deadline = TestData.Deadline(subject.Id, DateTimeOffset.UtcNow.AddDays(5), linkedFileRecordId: fileRecord.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.FileRecords.Add(fileRecord);
            arrange.Deadlines.Add(deadline);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.FileRecords.Where(x => x.Id == fileRecord.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.Deadlines.SingleAsync(x => x.Id == deadline.Id);
        Assert.Null(survived.LinkedFileRecordId);
    }

    [Fact]
    public async Task Deleting_subject_nulls_file_record_subject_and_keeps_record()
    {
        var subject = TestData.Subject();
        var fileRecord = TestData.FileRecord("hash-2", subject.Id, FileRecordStatus.Sorted);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.FileRecords.Add(fileRecord);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Subjects.Where(x => x.Id == subject.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.FileRecords.SingleAsync(x => x.Id == fileRecord.Id);
        Assert.Null(survived.SubjectId);
    }
}
