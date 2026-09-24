using Microsoft.EntityFrameworkCore;

namespace StudComp.Data.Tests;

/// <summary>
/// Каскады картотеки (new_addons.md §3.3). Главный факт здесь — первый: карточка это невосполнимый
/// пользовательский текст, и удаление предмета не имеет права её унести.
/// </summary>
public sealed class CardsCascadeTests : DatabaseTestBase
{
    [Fact]
    public async Task Deleting_subject_nulls_card_link_and_keeps_card()
    {
        var subject = TestData.Subject();
        var card = TestData.Card(subject.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Subjects.Where(x => x.Id == subject.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.Cards.SingleAsync(x => x.Id == card.Id);
        Assert.Null(survived.SubjectId);
        Assert.Equal(card.Front, survived.Front);
        Assert.Equal(card.Back, survived.Back);
    }

    [Fact]
    public async Task Deleting_deck_nulls_card_link_and_keeps_card()
    {
        var deck = TestData.CardDeck();
        var card = TestData.Card(deckId: deck.Id);

        await using (var arrange = CreateContext())
        {
            arrange.CardDecks.Add(deck);
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.CardDecks.Where(x => x.Id == deck.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.Cards.SingleAsync(x => x.Id == card.Id);
        Assert.Null(survived.DeckId);
    }

    [Fact]
    public async Task Deleting_source_note_and_file_nulls_links_and_keeps_card()
    {
        var subject = TestData.Subject();
        var note = TestData.Note(subject.Id);
        var file = TestData.FileRecord("hash-1");
        var card = TestData.Card(subject.Id, sourceNoteId: note.Id, sourceFileRecordId: file.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.Notes.Add(note);
            arrange.FileRecords.Add(file);
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Notes.Where(x => x.Id == note.Id).ExecuteDeleteAsync();
            await act.FileRecords.Where(x => x.Id == file.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.Cards.SingleAsync(x => x.Id == card.Id);
        Assert.Null(survived.SourceNoteId);
        Assert.Null(survived.SourceFileRecordId);
        Assert.Equal(card.Back, survived.Back);
    }

    [Fact]
    public async Task Deleting_card_cascades_tag_links_and_review_logs()
    {
        var card = TestData.Card();
        var tag = TestData.CardTag();
        var session = TestData.StudySession();
        var log = TestData.CardReviewLog(card.Id, session.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            arrange.CardTags.Add(tag);
            arrange.StudySessions.Add(session);
            arrange.CardTagLinks.Add(new Core.Domain.CardTagLink { CardId = card.Id, TagId = tag.Id });
            arrange.CardReviewLogs.Add(log);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Cards.Where(x => x.Id == card.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        Assert.Empty(await assert.CardTagLinks.Where(x => x.CardId == card.Id).ToListAsync());
        Assert.Empty(await assert.CardReviewLogs.Where(x => x.CardId == card.Id).ToListAsync());

        // Сама метка при этом переживает удаление карточки — она глобальная.
        Assert.NotNull(await assert.CardTags.SingleOrDefaultAsync(x => x.Id == tag.Id));
    }

    [Fact]
    public async Task Deleting_tag_cascades_links_and_keeps_card()
    {
        var card = TestData.Card();
        var tag = TestData.CardTag();

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            arrange.CardTags.Add(tag);
            arrange.CardTagLinks.Add(new Core.Domain.CardTagLink { CardId = card.Id, TagId = tag.Id });
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.CardTags.Where(x => x.Id == tag.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        Assert.Empty(await assert.CardTagLinks.Where(x => x.TagId == tag.Id).ToListAsync());
        Assert.NotNull(await assert.Cards.SingleOrDefaultAsync(x => x.Id == card.Id));
    }

    [Fact]
    public async Task Deleting_session_nulls_review_log_link_and_keeps_history()
    {
        var card = TestData.Card();
        var session = TestData.StudySession();
        var log = TestData.CardReviewLog(card.Id, session.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            arrange.StudySessions.Add(session);
            arrange.CardReviewLogs.Add(log);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.StudySessions.Where(x => x.Id == session.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        var survived = await assert.CardReviewLogs.SingleAsync(x => x.Id == log.Id);
        Assert.Null(survived.SessionId);
        Assert.Equal(card.Id, survived.CardId);
    }

    [Fact]
    public async Task Deleting_subject_keeps_deck_and_session_history()
    {
        var subject = TestData.Subject();
        var deck = TestData.CardDeck(subject.Id);
        var session = TestData.StudySession(subject.Id, deck.Id);

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.CardDecks.Add(deck);
            arrange.StudySessions.Add(session);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Subjects.Where(x => x.Id == subject.Id).ExecuteDeleteAsync();
        }

        await using var assert = CreateContext();
        Assert.Null((await assert.CardDecks.SingleAsync(x => x.Id == deck.Id)).SubjectId);
        Assert.Null((await assert.StudySessions.SingleAsync(x => x.Id == session.Id)).SubjectId);
    }

    [Fact]
    public async Task Tag_name_is_unique()
    {
        await using var context = CreateContext();
        context.CardTags.Add(TestData.CardTag("формулы"));
        await context.SaveChangesAsync();

        context.CardTags.Add(TestData.CardTag("формулы"));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
