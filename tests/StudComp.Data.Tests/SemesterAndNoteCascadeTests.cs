using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Tests;

/// <summary>
/// Каскады сущностей Phase 12.3 (new_addons.md §5). Общее правило здесь строже, чем у расписания и
/// оценок: заметка — авторский текст пользователя, и ни удаление предмета, ни удаление файла или
/// пары не имеют права её уносить (ARCHITECTURE §7.2, §14 по духу).
/// </summary>
public sealed class SemesterAndNoteCascadeTests : DatabaseTestBase
{
    [Fact]
    public async Task Deleting_semester_nulls_subject_link_and_keeps_subject_with_children()
    {
        var semester = TestData.Semester();
        var subject = TestData.Subject();
        subject.SemesterId = semester.Id;
        var entry = TestData.ScheduleEntry(subject.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Semesters.Add(semester);
            arrange.Subjects.Add(subject);
            arrange.ScheduleEntries.Add(entry);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Semesters.Where(x => x.Id == semester.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.Subjects.SingleAsync(x => x.Id == subject.Id);
        Assert.Null(survived.SemesterId);
        Assert.Single(assert.ScheduleEntries);
    }

    [Fact]
    public async Task Deleting_subject_nulls_note_link_and_keeps_note()
    {
        var subject = TestData.Subject();
        var note = TestData.Note(subject.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.Notes.Add(note);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Subjects.Where(x => x.Id == subject.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.Notes.SingleAsync(x => x.Id == note.Id);
        Assert.Null(survived.SubjectId);
        Assert.Equal(note.ContentMarkdown, survived.ContentMarkdown);
    }

    [Fact]
    public async Task Deleting_schedule_entry_nulls_note_link_and_keeps_note()
    {
        var subject = TestData.Subject();
        var entry = TestData.ScheduleEntry(subject.Id);
        var note = TestData.Note(subject.Id, scheduleEntryId: entry.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.ScheduleEntries.Add(entry);
            arrange.Notes.Add(note);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.ScheduleEntries.Where(x => x.Id == entry.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.Notes.SingleAsync(x => x.Id == note.Id);
        Assert.Null(survived.ScheduleEntryId);
    }

    [Fact]
    public async Task Deleting_file_record_nulls_note_link_and_keeps_note()
    {
        var subject = TestData.Subject();
        var fileRecord = TestData.FileRecord("hash-note", subject.Id, FileRecordStatus.Sorted);
        var note = TestData.Note(subject.Id, fileRecordId: fileRecord.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.FileRecords.Add(fileRecord);
            arrange.Notes.Add(note);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.FileRecords.Where(x => x.Id == fileRecord.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.Notes.SingleAsync(x => x.Id == note.Id);
        Assert.Null(survived.LinkedFileRecordId);
    }

    [Fact]
    public async Task Semester_dates_round_trip_through_sqlite()
    {
        var semester = TestData.Semester();

        await using (var arrange = CreateContext())
        {
            arrange.Semesters.Add(semester);
            await arrange.SaveChangesAsync();
        }

        await using var assert = CreateContext();
        var loaded = await assert.Semesters.SingleAsync();

        // DateOnly провайдер SQLite кладёт в TEXT сам — конвертер не нужен, но убедиться стоит.
        Assert.Equal(new DateOnly(2026, 9, 1), loaded.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 27), loaded.EndDate);
    }

    [Fact]
    public async Task Semester_without_end_date_round_trips_as_null()
    {
        var semester = TestData.Semester();
        semester.EndDate = null;

        await using (var arrange = CreateContext())
        {
            arrange.Semesters.Add(semester);
            await arrange.SaveChangesAsync();
        }

        await using var assert = CreateContext();
        Assert.Null((await assert.Semesters.SingleAsync()).EndDate);
    }
}
