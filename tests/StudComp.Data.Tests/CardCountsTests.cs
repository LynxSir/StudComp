using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Счётчики карточек: бейдж очереди повторения и числа в рельсе фильтров (new_addons.md §2.1, §8.2).
/// Считает их база одним запросом — на открытии раздела счётчик на каждый пункт превратился бы в N+1.
/// </summary>
public sealed class CardCountsTests : DatabaseTestBase
{
    [Fact]
    public async Task Due_count_takes_only_live_cards_whose_time_has_come()
    {
        var due = Card(dueInDays: -1);
        var dueRightNow = Card(dueInDays: 0);
        var later = Card(dueInDays: 3);
        var newCard = Card();

        var suspended = Card(dueInDays: -5);
        suspended.IsSuspended = true;

        var trashed = Card(dueInDays: -5);
        trashed.DeletedAt = DateTimeOffset.UtcNow;

        await SeedAsync(due, dueRightNow, later, newCard, suspended, trashed);

        var repository = new CardRepository(Factory);

        // Отложенная в тренировку не попадёт, удалённой не существует — звать к ним нельзя.
        Assert.Equal(2, await repository.CountDueAsync(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Counts_by_subject_skip_cards_without_a_subject_and_from_the_trash()
    {
        var math = TestData.Subject("Матан");
        var physics = TestData.Subject("Физика");
        await SeedSubjectsAsync(math, physics);

        var trashed = Card(subjectId: math.Id);
        trashed.DeletedAt = DateTimeOffset.UtcNow;

        await SeedAsync(
            Card(subjectId: math.Id),
            Card(subjectId: math.Id),
            Card(subjectId: physics.Id),
            Card(),
            trashed);

        var counts = await new CardRepository(Factory).CountsBySubjectAsync();

        Assert.Equal(2, counts[math.Id]);
        Assert.Equal(1, counts[physics.Id]);
        Assert.Equal(2, counts.Count);
    }

    [Fact]
    public async Task Counts_by_deck_work_the_same_way()
    {
        var deck = TestData.CardDeck();
        await using (var context = CreateContext())
        {
            context.CardDecks.Add(deck);
            await context.SaveChangesAsync();
        }

        await SeedAsync(Card(deckId: deck.Id), Card(deckId: deck.Id), Card());

        var counts = await new CardRepository(Factory).CountsByDeckAsync();

        Assert.Equal(2, counts[deck.Id]);
        Assert.Single(counts);
    }

    [Fact]
    public async Task Counts_are_empty_on_an_empty_card_index()
    {
        var repository = new CardRepository(Factory);

        Assert.Equal(0, await repository.CountDueAsync(DateTimeOffset.UtcNow));
        Assert.Empty(await repository.CountsBySubjectAsync());
        Assert.Empty(await repository.CountsByDeckAsync());
    }

    private static Card Card(Guid? subjectId = null, Guid? deckId = null, int? dueInDays = null)
    {
        var card = TestData.Card(subjectId: subjectId, deckId: deckId);
        card.DueAt = dueInDays is { } days ? DateTimeOffset.UtcNow.AddDays(days) : null;
        return card;
    }

    private async Task SeedSubjectsAsync(params Subject[] subjects)
    {
        await using var context = CreateContext();
        context.Subjects.AddRange(subjects);
        await context.SaveChangesAsync();
    }

    private async Task SeedAsync(params Card[] cards)
    {
        await using var context = CreateContext();
        context.Cards.AddRange(cards);
        await context.SaveChangesAsync();
    }
}
