using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Черновик текста отчёта (<see cref="NoteKind.ReportDraft"/>) – служебная заметка и обязан быть
/// невидим в общих списках заметок: иначе он мелькал бы в «Заметках» Хаба, в зоне Дашборда
/// «Последние заметки» и в диалоге «Собрать из заметок», которые читают ровно эти два запроса.
/// </summary>
public sealed class ReportDraftFilterTests : DatabaseTestBase
{
    [Fact]
    public async Task Report_draft_is_absent_from_both_note_listings()
    {
        var subject = TestData.Subject();

        var ordinary = TestData.Note(subject.Id);

        var draft = TestData.Note(subject.Id);
        draft.Kind = NoteKind.ReportDraft;
        draft.Title = "Черновик отчёта";

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.Notes.AddRange(ordinary, draft);
            await arrange.SaveChangesAsync();
        }

        var repository = new NoteRepository(Factory);

        Assert.Equal([ordinary.Id], (await repository.GetBySubjectAsync(subject.Id)).Select(note => note.Id));
        Assert.Equal([ordinary.Id], (await repository.GetRecentAsync(10)).Select(note => note.Id));
    }

    [Fact]
    public async Task Report_draft_is_found_by_its_own_query_per_subject()
    {
        var first = TestData.Subject();
        var second = TestData.Subject();

        var forFirst = TestData.Note(first.Id);
        forFirst.Kind = NoteKind.ReportDraft;

        var withoutSubject = TestData.Note();
        withoutSubject.Kind = NoteKind.ReportDraft;

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.AddRange(first, second);
            arrange.Notes.AddRange(forFirst, withoutSubject);
            await arrange.SaveChangesAsync();
        }

        var repository = new NoteRepository(Factory);

        Assert.Equal(forFirst.Id, (await repository.GetReportDraftAsync(first.Id))!.Id);
        Assert.Equal(withoutSubject.Id, (await repository.GetReportDraftAsync(null))!.Id);

        // У предмета без черновика его и нет – сервис на этом и строит «найти или создать».
        Assert.Null(await repository.GetReportDraftAsync(second.Id));
    }

    [Fact]
    public async Task Ordinary_note_is_never_mistaken_for_a_draft()
    {
        var subject = TestData.Subject();
        var ordinary = TestData.Note(subject.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.Notes.Add(ordinary);
            await arrange.SaveChangesAsync();
        }

        Assert.Null(await new NoteRepository(Factory).GetReportDraftAsync(subject.Id));
    }
}
