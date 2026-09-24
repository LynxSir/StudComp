using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// То, чем библиотека Картотеки пользуется в Phase 12.7: выдача со снипетами, точечное сохранение
/// правки, массовые операции над выделением и счётчики (new_addons.md §8.2).
/// </summary>
public sealed class CardServiceLibraryTests : CardsDatabaseTestBase
{
    [Fact]
    public async Task Detailed_search_brings_the_snippet_with_highlight_markers()
    {
        await SeedCardAsync("Теорема Стокса", "Связывает поток ротора и циркуляцию по контуру.");

        var found = await Cards.SearchDetailedAsync("циркуляцию", new CardSearchOptions());

        var hit = Assert.Single(found);
        Assert.Contains(CardHighlight.MarkerStart, hit.Snippet, StringComparison.Ordinal);
        Assert.Contains(CardHighlight.MarkerEnd, hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detailed_search_keeps_the_same_order_as_the_plain_one()
    {
        await SeedCardAsync("Интеграл Римана");
        await SeedCardAsync("Интеграл Лебега");
        await SeedCardAsync("Интегральная сумма");

        var plain = await Cards.SearchAsync("интеграл", new CardSearchOptions());
        var detailed = await Cards.SearchDetailedAsync("интеграл", new CardSearchOptions());

        Assert.Equal(plain.Select(x => x.Id), detailed.Select(x => x.Card.Id));
    }

    [Fact]
    public async Task Updating_content_does_not_touch_the_tags()
    {
        var subjectId = await SeedSubjectAsync();
        var id = await SeedCardAsync(tags: ["формулы", "экзамен"]);

        var result = await Cards.UpdateContentAsync(
            id,
            "Теорема Стокса (уточнённая)",
            "Новый текст оборота.",
            hint: "Через дифференциальные формы",
            source: "Лекция 12.09",
            CardKind.Question,
            CardDifficulty.Hard,
            subjectId,
            deckId: null);

        Assert.True(result.IsSuccess);

        var card = await Cards.GetByIdAsync(id);
        Assert.NotNull(card);
        Assert.Equal("Теорема Стокса (уточнённая)", card.Front);
        Assert.Equal(CardKind.Question, card.Kind);
        Assert.Equal(CardDifficulty.Hard, card.Difficulty);
        Assert.Equal(subjectId, card.SubjectId);

        var tags = await Tags.GetForCardsAsync([id]);
        Assert.Equal(2, tags[id].Count);
    }

    [Fact]
    public async Task Updating_a_missing_card_fails_without_throwing()
    {
        var result = await Cards.UpdateContentAsync(
            Guid.NewGuid(),
            "Что угодно",
            string.Empty,
            null,
            null,
            CardKind.Term,
            CardDifficulty.Normal,
            null,
            null);

        Assert.True(result.IsFailure);
        Assert.Equal("cards.card_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Setting_tags_replaces_the_whole_set()
    {
        var id = await SeedCardAsync(tags: ["формулы", "экзамен"]);

        Assert.True((await Cards.SetTagsAsync(id, ["термины"])).IsSuccess);

        var tags = await Tags.GetForCardsAsync([id]);
        Assert.Equal(["термины"], tags[id].Select(x => x.Name));
    }

    [Fact]
    public async Task Tags_are_added_to_the_whole_selection_and_adding_twice_changes_nothing()
    {
        var first = await SeedCardAsync("Первая");
        var second = await SeedCardAsync("Вторая");

        Assert.True((await Cards.AddTagsAsync([first, second], ["к экзамену"])).IsSuccess);
        Assert.True((await Cards.AddTagsAsync([first, second], ["к экзамену"])).IsSuccess);

        var tags = await Tags.GetForCardsAsync([first, second]);
        Assert.Single(tags[first]);
        Assert.Single(tags[second]);
    }

    [Fact]
    public async Task Removing_a_tag_leaves_the_others_and_ignores_unknown_names()
    {
        var id = await SeedCardAsync(tags: ["формулы", "экзамен"]);

        Assert.True((await Cards.RemoveTagsAsync([id], ["формулы", "такой метки нет"])).IsSuccess);

        var tags = await Tags.GetForCardsAsync([id]);
        Assert.Equal(["экзамен"], tags[id].Select(x => x.Name));
    }

    [Fact]
    public async Task Bulk_pin_and_suspend_touch_every_selected_card()
    {
        var first = await SeedCardAsync("Первая");
        var second = await SeedCardAsync("Вторая");

        Assert.Equal(2, (await Cards.SetPinnedManyAsync([first, second], true)).Value);
        Assert.Equal(2, (await Cards.SetSuspendedManyAsync([first, second], true)).Value);

        foreach (var id in new[] { first, second })
        {
            var card = await Cards.GetByIdAsync(id);
            Assert.NotNull(card);
            Assert.True(card.IsPinned);
            Assert.True(card.IsSuspended);
        }
    }

    [Fact]
    public async Task Empty_selection_is_a_no_op_for_bulk_operations()
    {
        Assert.Equal(0, (await Cards.AddTagsAsync([], ["метка"])).Value);
        Assert.Equal(0, (await Cards.RemoveTagsAsync([], ["метка"])).Value);
        Assert.Equal(0, (await Cards.SetPinnedManyAsync([], true)).Value);
    }

    [Fact]
    public async Task Due_count_matches_what_the_badge_should_show()
    {
        var due = await SeedCardAsync("Пора повторить");
        await SeedCardAsync("Новая");

        await MakeDueAsync(due, TimeSpan.FromHours(-1));

        Assert.Equal(1, await Cards.CountDueAsync());
    }

    [Fact]
    public async Task Counts_for_the_rail_come_grouped()
    {
        var subjectId = await SeedSubjectAsync();
        var deckId = await SeedDeckAsync(subjectId: subjectId);

        await SeedCardAsync("Первая", subjectId: subjectId, deckId: deckId);
        await SeedCardAsync("Вторая", subjectId: subjectId);
        await SeedCardAsync("Без предмета");

        var bySubject = await Cards.CountsBySubjectAsync();
        var byDeck = await Cards.CountsByDeckAsync();

        Assert.Equal(2, bySubject[subjectId]);
        Assert.Equal(1, byDeck[deckId]);
    }

    [Fact]
    public async Task Card_of_the_day_is_the_same_within_one_day_and_changes_with_the_date()
    {
        for (var i = 0; i < 6; i++)
        {
            await SeedCardAsync($"Карточка {i}");
        }

        var day = new DateOnly(2026, 9, 7);

        var first = await Cards.GetCardOfTheDayAsync(day);
        var again = await Cards.GetCardOfTheDayAsync(day);

        Assert.NotNull(first);
        Assert.Equal(first.Id, again?.Id);

        // Соседние дни обязаны давать разные карточки, иначе «карточка дня» вырождается в одну.
        var tomorrow = await Cards.GetCardOfTheDayAsync(day.AddDays(1));
        Assert.NotEqual(first.Id, tomorrow?.Id);
    }

    [Fact]
    public async Task Card_of_the_day_prefers_the_ones_already_forgotten()
    {
        await SeedCardAsync("Крепко выученная");
        var weakId = await SeedCardAsync("Та, которую забывали");

        await MakeWeakAsync(weakId);

        var card = await Cards.GetCardOfTheDayAsync(new DateOnly(2026, 9, 7));

        Assert.Equal(weakId, card?.Id);
    }

    [Fact]
    public async Task Card_of_the_day_is_absent_on_an_empty_index()
    {
        Assert.Null(await Cards.GetCardOfTheDayAsync(DateOnly.FromDateTime(DateTime.Today)));
    }

    private async Task MakeDueAsync(Guid id, TimeSpan offset)
    {
        var card = await CardRepo.GetByIdAsync(id);
        Assert.NotNull(card);

        card.DueAt = DateTimeOffset.Now + offset;
        card.Repetitions = 1;
        await CardRepo.UpdateAsync(card);
    }

    private async Task MakeWeakAsync(Guid id)
    {
        var card = await CardRepo.GetByIdAsync(id);
        Assert.NotNull(card);

        card.Lapses = 3;
        await CardRepo.UpdateAsync(card);
    }
}
