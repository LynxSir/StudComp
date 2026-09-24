using StudComp.Core.Domain;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Колоды (new_addons.md §3.1, §13.8). Проверяется оба поведения одного типа: обычная колода
/// хранит карточки, умная подборка вычисляет их сохранённым запросом.
/// </summary>
public sealed class CardDeckServiceTests : CardsDatabaseTestBase
{
    [Fact]
    public async Task Create_read_update_delete_round_trips()
    {
        var subjectId = await SeedSubjectAsync();
        var id = await SeedDeckAsync("К экзамену", subjectId);

        var deck = await Decks.GetByIdAsync(id);
        Assert.NotNull(deck);
        Assert.Equal("К экзамену", deck.Name);
        Assert.Equal(subjectId, deck.SubjectId);
        Assert.Single(await Decks.GetBySubjectAsync(subjectId));

        deck.Name = "К пересдаче";
        Assert.True((await Decks.UpdateAsync(deck)).IsSuccess);
        Assert.Equal("К пересдаче", (await Decks.GetByIdAsync(id))!.Name);

        Assert.True((await Decks.DeleteAsync(id)).IsSuccess);
        Assert.Empty(await Decks.GetAllAsync());
    }

    [Fact]
    public async Task Empty_name_is_rejected()
    {
        var result = await Decks.CreateAsync(new CardDeck { Name = "   " });

        Assert.True(result.IsFailure);
        Assert.Equal("cards.deck_name_required", result.Error.Code);
    }

    [Fact]
    public async Task Unknown_subject_is_rejected()
    {
        var result = await Decks.CreateAsync(new CardDeck { Name = "К экзамену", SubjectId = Guid.NewGuid() });

        Assert.True(result.IsFailure);
        Assert.Equal("cards.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Update_and_delete_of_a_missing_deck_are_rejected()
    {
        var update = await Decks.UpdateAsync(new CardDeck { Id = Guid.NewGuid(), Name = "Нет такой" });
        var delete = await Decks.DeleteAsync(Guid.NewGuid());

        Assert.Equal("cards.deck_not_found", update.Error.Code);
        Assert.Equal("cards.deck_not_found", delete.Error.Code);
    }

    [Fact]
    public async Task Deleting_a_deck_keeps_its_cards()
    {
        var deckId = await SeedDeckAsync();
        var cardId = await SeedCardAsync("Теорема Стокса", deckId: deckId);

        await Decks.DeleteAsync(deckId);

        var card = await Cards.GetByIdAsync(cardId);
        Assert.NotNull(card);
        Assert.Null(card.DeckId);
    }

    [Fact]
    public async Task Ordinary_deck_returns_the_cards_put_into_it()
    {
        var deckId = await SeedDeckAsync();
        var inside = await SeedCardAsync("Теорема Стокса", deckId: deckId);
        await SeedCardAsync("Теорема Гаусса");

        var cards = await Decks.GetCardsAsync(deckId);

        Assert.Equal(inside, Assert.Single(cards).Id);
    }

    [Fact]
    public async Task Smart_deck_computes_its_content_from_the_saved_query()
    {
        var subjectId = await SeedSubjectAsync();
        var smartId = await SeedDeckAsync("Трудные формулы", query: "#формулы трудные");

        var matching = await SeedCardAsync("Теорема Стокса", subjectId: subjectId, tags: ["формулы"]);
        await SeedCardAsync("Теорема Гаусса", subjectId: subjectId, tags: ["формулы"]);
        await SeedCardAsync("Что такое семафор?", subjectId: subjectId, tags: ["теория"]);

        // «Трудная» — это та, которую уже забывали; отметим только одну.
        await using (var context = CreateContext())
        {
            var card = context.Cards.Single(x => x.Id == matching);
            card.Lapses = 2;
            await context.SaveChangesAsync();
        }

        var cards = await Decks.GetCardsAsync(smartId);

        Assert.Equal(matching, Assert.Single(cards).Id);

        // Ни одна карточка при этом в колоду не «положена» — подборка вычисляемая.
        Assert.All(cards, card => Assert.Null(card.DeckId));
    }

    [Fact]
    public async Task Smart_deck_follows_the_cards_as_they_change()
    {
        var smartId = await SeedDeckAsync("Формулы", query: "#формулы");
        var cardId = await SeedCardAsync("Теорема Стокса", tags: ["теория"]);

        Assert.Empty(await Decks.GetCardsAsync(smartId));

        var card = await Cards.GetByIdAsync(cardId);
        await Cards.UpdateAsync(card!, ["формулы"]);

        Assert.Single(await Decks.GetCardsAsync(smartId));
    }

    [Fact]
    public async Task Blank_query_means_an_ordinary_deck()
    {
        var id = await SeedDeckAsync("К экзамену", query: "   ");

        Assert.Null((await Decks.GetByIdAsync(id))!.QueryExpression);
    }

    [Fact]
    public async Task Cards_of_a_missing_deck_are_an_empty_list_not_a_crash()
    {
        Assert.Empty(await Decks.GetCardsAsync(Guid.NewGuid()));
    }
}
