using StudComp.Core.Domain;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Заметки (new_addons.md §1.9): CRUD, автосохранение и порядок выдачи.
/// </summary>
public sealed class NoteServiceTests : OrganizerDatabaseTestBase
{
    private static Note New(Guid? subjectId = null, string title = "Лекция 1") => new()
    {
        SubjectId = subjectId,
        Kind = NoteKind.Lecture,
        Title = title,
        ContentMarkdown = "Текст",
    };

    [Fact]
    public async Task Create_read_delete_round_trips()
    {
        var subjectId = await SeedSubjectAsync();

        var created = await Notes.CreateAsync(New(subjectId));
        Assert.True(created.IsSuccess);

        var loaded = await Notes.GetByIdAsync(created.Value);
        Assert.Equal("Лекция 1", loaded!.Title);
        Assert.Equal(subjectId, loaded.SubjectId);

        Assert.True((await Notes.DeleteAsync(created.Value)).IsSuccess);
        Assert.Null(await Notes.GetByIdAsync(created.Value));
    }

    [Fact]
    public async Task Create_stamps_both_timestamps()
    {
        var id = (await Notes.CreateAsync(New())).Value;

        var note = await Notes.GetByIdAsync(id);

        Assert.NotEqual(default, note!.CreatedAt);
        Assert.NotEqual(default, note.UpdatedAt);
    }

    [Fact]
    public async Task Create_with_unknown_subject_is_rejected()
    {
        var result = await Notes.CreateAsync(New(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("organizer.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Note_without_subject_is_allowed()
    {
        var result = await Notes.CreateAsync(New());

        Assert.True(result.IsSuccess);
        Assert.Null((await Notes.GetByIdAsync(result.Value))!.SubjectId);
    }

    [Fact]
    public async Task Blank_title_falls_back_instead_of_failing()
    {
        // Пустой заголовок — обычное состояние только что созданной заметки при автосохранении.
        var note = New();
        note.Title = "   ";

        var id = (await Notes.CreateAsync(note)).Value;

        Assert.Equal("Без названия", (await Notes.GetByIdAsync(id))!.Title);
    }

    [Fact]
    public async Task Save_content_updates_text_and_timestamp_only()
    {
        var subjectId = await SeedSubjectAsync();
        var fileRecordId = await SeedFileRecordAsync();

        var note = New(subjectId);
        note.LinkedFileRecordId = fileRecordId;
        note.LinkedPath = @"Лекции\lecture.pdf";
        note.IsPinned = true;
        var id = (await Notes.CreateAsync(note)).Value;
        var before = (await Notes.GetByIdAsync(id))!;

        await Task.Delay(10);
        var result = await Notes.UpdateContentAsync(id, "Лекция 2", "Новый текст");

        Assert.True(result.IsSuccess);
        var after = (await Notes.GetByIdAsync(id))!;
        Assert.Equal("Лекция 2", after.Title);
        Assert.Equal("Новый текст", after.ContentMarkdown);
        Assert.True(after.UpdatedAt >= before.UpdatedAt);

        // Автосохранение не имеет права трогать привязки заметки.
        Assert.Equal(subjectId, after.SubjectId);
        Assert.Equal(fileRecordId, after.LinkedFileRecordId);
        Assert.Equal(@"Лекции\lecture.pdf", after.LinkedPath);
        Assert.True(after.IsPinned);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
    }

    [Fact]
    public async Task Missing_note_is_reported_not_thrown()
    {
        var ghost = Guid.NewGuid();

        Assert.Equal("organizer.note_not_found", (await Notes.UpdateContentAsync(ghost, "x", "y")).Error.Code);
        Assert.Equal("organizer.note_not_found", (await Notes.SetPinnedAsync(ghost, true)).Error.Code);
        Assert.Equal("organizer.note_not_found", (await Notes.DeleteAsync(ghost)).Error.Code);
    }

    [Fact]
    public async Task Pinned_notes_come_first_in_subject_list()
    {
        var subjectId = await SeedSubjectAsync();
        var first = (await Notes.CreateAsync(New(subjectId, "Первая"))).Value;
        await Task.Delay(10);
        var second = (await Notes.CreateAsync(New(subjectId, "Вторая"))).Value;

        await Notes.SetPinnedAsync(first, true);

        var list = await Notes.GetBySubjectAsync(subjectId);

        Assert.Equal(first, list[0].Id);
        Assert.Equal(second, list[1].Id);
    }

    [Fact]
    public async Task Subject_list_is_ordered_by_last_edit()
    {
        var subjectId = await SeedSubjectAsync();
        var older = (await Notes.CreateAsync(New(subjectId, "Старая"))).Value;
        await Task.Delay(10);
        var newer = (await Notes.CreateAsync(New(subjectId, "Новая"))).Value;

        await Task.Delay(10);
        await Notes.UpdateContentAsync(older, "Старая", "правка");

        var list = await Notes.GetBySubjectAsync(subjectId);

        Assert.Equal(older, list[0].Id);
        Assert.Equal(newer, list[1].Id);
    }

    [Fact]
    public async Task Recent_returns_newest_first_and_respects_take()
    {
        var subjectId = await SeedSubjectAsync();
        for (var i = 0; i < 4; i++)
        {
            await Notes.CreateAsync(New(subjectId, $"Заметка {i}"));
            await Task.Delay(5);
        }

        var recent = await Notes.GetRecentAsync(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("Заметка 3", recent[0].Title);
    }

    [Fact]
    public async Task Notes_can_be_found_by_linked_path()
    {
        var subjectId = await SeedSubjectAsync();
        var note = New(subjectId, "Пометка к файлу");
        note.Kind = NoteKind.FileNote;
        note.LinkedPath = @"Матан\Лекции\lecture.pdf";
        await Notes.CreateAsync(note);

        var found = await Notes.GetByLinkedPathAsync(@"Матан\Лекции\lecture.pdf");

        Assert.Single(found);
        Assert.Empty(await Notes.GetByLinkedPathAsync(@"Матан\Лекции\other.pdf"));
        Assert.Empty(await Notes.GetByLinkedPathAsync("  "));
    }

    [Fact]
    public async Task Deleting_subject_keeps_the_note()
    {
        var subjectId = await SeedSubjectAsync();
        var id = (await Notes.CreateAsync(New(subjectId))).Value;

        await Subjects.DeleteAsync(subjectId);

        // Заметка — авторский текст пользователя; предмет уходит, конспект остаётся (§14 по духу).
        var survived = await Notes.GetByIdAsync(id);
        Assert.NotNull(survived);
        Assert.Null(survived.SubjectId);
    }
}
