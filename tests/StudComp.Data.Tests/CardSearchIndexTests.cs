using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Страж триггеров поискового индекса (new_addons.md §4.2). Ради этого набора триггеры и выбраны
/// вместо поддержки индекса кодом репозитория: EF пишет в <c>Cards</c> тремя разными путями, и
/// любой обход <c>SaveChanges</c> обязан отражаться в поиске так же немедленно.
/// </summary>
/// <remarks>
/// Он же ловит забывчивость будущих миграций: SQLite-ребилд таблицы сносит триггеры вместе со
/// старой таблицей, и если их не пересоздать в <c>Up()</c>, эти факты покраснеют.
/// </remarks>
public sealed class CardSearchIndexTests : DatabaseTestBase
{
    private ICardSearchRepository Search => new CardSearchRepository(Factory);

    [Fact]
    public async Task Card_saved_through_ef_is_searchable_immediately()
    {
        var card = TestData.Card(front: "Теорема Стокса", back: "Связывает поток ротора и циркуляцию.");

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        Assert.Contains(card.Id, await FindAsync("стокса"));
        Assert.Contains(card.Id, await FindAsync("циркуляцию"));
    }

    [Fact]
    public async Task Update_through_save_changes_replaces_the_indexed_text()
    {
        var card = TestData.Card(front: "Теорема Стокса");

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            var tracked = await act.Cards.SingleAsync(x => x.Id == card.Id);
            tracked.Front = "Теорема Гаусса";
            await act.SaveChangesAsync();
        }

        Assert.Contains(card.Id, await FindAsync("гаусса"));
        Assert.DoesNotContain(card.Id, await FindAsync("стокса"));
    }

    [Fact]
    public async Task Update_through_execute_update_reaches_the_index_too()
    {
        var card = TestData.Card(front: "Теорема Стокса");

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        // Мимо SaveChanges: именно этот путь и разъехался бы с индексом, поддерживай мы его кодом.
        await using (var act = CreateContext())
        {
            await act.Cards
                .Where(x => x.Id == card.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Front, "Теорема Грина"));
        }

        Assert.Contains(card.Id, await FindAsync("грина"));
        Assert.DoesNotContain(card.Id, await FindAsync("стокса"));
    }

    [Fact]
    public async Task Delete_through_execute_delete_removes_the_row_from_the_index()
    {
        var card = TestData.Card(front: "Теорема Стокса");

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Cards.Where(x => x.Id == card.Id).ExecuteDeleteAsync();
        }

        Assert.Empty(await FindAsync("стокса"));
        Assert.Equal(0, await CountIndexRowsAsync());
    }

    [Fact]
    public async Task Tag_added_and_removed_changes_what_the_search_returns()
    {
        var card = TestData.Card(front: "Теорема Стокса");
        var tag = TestData.CardTag("векторный");

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            arrange.CardTags.Add(tag);
            await arrange.SaveChangesAsync();
        }

        Assert.Empty(await FindAsync("векторный"));

        await using (var act = CreateContext())
        {
            act.CardTagLinks.Add(new CardTagLink { CardId = card.Id, TagId = tag.Id });
            await act.SaveChangesAsync();
        }

        Assert.Contains(card.Id, await FindAsync("векторный"));

        await using (var act = CreateContext())
        {
            await act.CardTagLinks
                .Where(x => x.CardId == card.Id && x.TagId == tag.Id)
                .ExecuteDeleteAsync();
        }

        Assert.Empty(await FindAsync("векторный"));
    }

    [Fact]
    public async Task Renaming_a_subject_is_picked_up_by_the_index()
    {
        var subject = TestData.Subject("Математический анализ");
        var card = TestData.Card(subject.Id, front: "Теорема Стокса");

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        Assert.Contains(card.Id, await FindAsync("математический"));

        await using (var act = CreateContext())
        {
            await act.Subjects
                .Where(x => x.Id == subject.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, "Функциональный анализ"));
        }

        Assert.Contains(card.Id, await FindAsync("функциональный"));
        Assert.Empty(await FindAsync("математический"));
    }

    [Fact]
    public async Task Renaming_a_deck_is_picked_up_by_the_index()
    {
        var deck = TestData.CardDeck(name: "Коллоквиум");
        var card = TestData.Card(deckId: deck.Id, front: "Теорема Стокса");

        await using (var arrange = CreateContext())
        {
            arrange.CardDecks.Add(deck);
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        Assert.Contains(card.Id, await FindAsync("коллоквиум"));

        await using (var act = CreateContext())
        {
            var tracked = await act.CardDecks.SingleAsync(x => x.Id == deck.Id);
            tracked.Name = "Пересдача";
            await act.SaveChangesAsync();
        }

        Assert.Contains(card.Id, await FindAsync("пересдача"));
        Assert.Empty(await FindAsync("коллоквиум"));
    }

    [Fact]
    public async Task Soft_deleted_card_leaves_the_search_but_stays_findable_in_the_trash()
    {
        var card = TestData.Card(front: "Теорема Стокса");

        await using (var arrange = CreateContext())
        {
            arrange.Cards.Add(card);
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Cards
                .Where(x => x.Id == card.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DeletedAt, DateTimeOffset.UtcNow));
        }

        Assert.Empty(await FindAsync("стокса"));

        // Строку из индекса мягкое удаление не выпиливает — её отсекает условие запроса, и это же
        // бесплатно даёт поиск внутри «Корзины» (new_addons.md §4.2).
        Assert.Contains(card.Id, await FindAsync("стокса корзина"));
        Assert.Equal(1, await CountIndexRowsAsync());
    }

    [Fact]
    public async Task Rebuild_restores_the_index_after_it_was_wiped()
    {
        var subject = TestData.Subject("Математический анализ");
        var deck = TestData.CardDeck(subject.Id, "Коллоквиум");
        var tag = TestData.CardTag("векторный");
        var card = TestData.Card(subject.Id, deck.Id, "Теорема Стокса");

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.CardDecks.Add(deck);
            arrange.CardTags.Add(tag);
            arrange.Cards.Add(card);
            arrange.CardTagLinks.Add(new CardTagLink { CardId = card.Id, TagId = tag.Id });
            await arrange.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            await act.Database.ExecuteSqlRawAsync("DELETE FROM CardSearch;");
        }

        Assert.Empty(await FindAsync("стокса"));

        var rebuilt = await Search.RebuildAsync();

        Assert.Equal(1, rebuilt);
        Assert.Contains(card.Id, await FindAsync("стокса"));
        Assert.Contains(card.Id, await FindAsync("векторный"));
        Assert.Contains(card.Id, await FindAsync("коллоквиум"));
        Assert.Contains(card.Id, await FindAsync("математический"));
    }

    [Fact]
    public async Task Full_text_index_is_reported_as_available_on_a_migrated_database()
    {
        Assert.True(await Search.IsFullTextAvailableAsync());
    }

    private async Task<IReadOnlyList<Guid>> FindAsync(string query)
    {
        var hits = await Search.SearchAsync(CardQuery.Parse(query), new CardSearchOptions());
        return hits.Select(x => x.CardId).ToList();
    }

    private async Task<int> CountIndexRowsAsync()
    {
        await using var context = CreateContext();
        var counts = await context.Database
            .SqlQuery<int>($"SELECT count(*) AS Value FROM CardSearch")
            .ToListAsync();
        return counts[0];
    }
}
