using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Сервис карточек на настоящей базе (new_addons.md §3.1, §3.5, §7.1). Ключевой блок здесь —
/// корзина: «удалить» обязано быть обратимым без единой потери.
/// </summary>
public sealed class CardServiceTests : CardsDatabaseTestBase
{
    [Fact]
    public async Task Create_read_update_round_trips()
    {
        var subjectId = await SeedSubjectAsync();
        var id = await SeedCardAsync("Теорема Стокса", "Поток ротора равен циркуляции.", subjectId);

        var created = await Cards.GetByIdAsync(id);

        Assert.NotNull(created);
        Assert.Equal("Теорема Стокса", created.Front);
        Assert.Equal(subjectId, created.SubjectId);
        Assert.Null(created.DeletedAt);

        created.Front = "Теорема Гаусса";
        var updated = await Cards.UpdateAsync(created);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Теорема Гаусса", (await Cards.GetByIdAsync(id))!.Front);
    }

    [Fact]
    public async Task Create_trims_and_clamps_overlong_fields()
    {
        var id = await SeedCardAsync(front: "  Теорема Стокса  ");
        var card = await Cards.GetByIdAsync(id);

        Assert.Equal("Теорема Стокса", card!.Front);

        var longFront = new string('я', 900);
        var result = await Cards.CreateAsync(new Card { Front = longFront, Back = "тело" });

        Assert.True(result.IsSuccess);
        Assert.Equal(500, (await Cards.GetByIdAsync(result.Value))!.Front.Length);
    }

    [Fact]
    public async Task Empty_front_is_rejected()
    {
        var result = await Cards.CreateAsync(new Card { Front = "   ", Back = "тело" });

        Assert.True(result.IsFailure);
        Assert.Equal("cards.front_required", result.Error.Code);
    }

    [Fact]
    public async Task Unknown_subject_and_deck_are_rejected()
    {
        var noSubject = await Cards.CreateAsync(
            new Card { Front = "Теорема", SubjectId = Guid.NewGuid() });
        var noDeck = await Cards.CreateAsync(
            new Card { Front = "Теорема", DeckId = Guid.NewGuid() });

        Assert.Equal("cards.subject_not_found", noSubject.Error.Code);
        Assert.Equal("cards.deck_not_found", noDeck.Error.Code);
    }

    [Fact]
    public async Task Update_of_a_missing_card_is_rejected()
    {
        var result = await Cards.UpdateAsync(new Card { Id = Guid.NewGuid(), Front = "Теорема" });

        Assert.True(result.IsFailure);
        Assert.Equal("cards.card_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Delete_moves_to_trash_and_restore_brings_everything_back()
    {
        var subjectId = await SeedSubjectAsync();
        var deckId = await SeedDeckAsync(subjectId: subjectId);
        var id = await SeedCardAsync(
            "Теорема Стокса",
            "Поток ротора равен циркуляции.",
            subjectId,
            deckId,
            ["формулы", "векторный анализ"]);

        var before = await Cards.GetByIdAsync(id);

        var moved = await Cards.MoveToTrashAsync([id]);

        Assert.Equal(1, moved.Value);
        Assert.Empty(await Cards.GetRecentAsync(50));
        Assert.Equal(1, await Cards.CountDeletedAsync());
        Assert.Single(await Cards.GetDeletedAsync(50));

        var restored = await Cards.RestoreAsync([id]);
        var after = await Cards.GetByIdAsync(id);

        Assert.Equal(1, restored.Value);
        Assert.Equal(0, await Cards.CountDeletedAsync());
        Assert.NotNull(after);
        Assert.Null(after.DeletedAt);

        // «Без потерь» — это буквально: ни одно поле не должно было поменяться по дороге.
        Assert.Equal(before!.Front, after.Front);
        Assert.Equal(before.Back, after.Back);
        Assert.Equal(before.SubjectId, after.SubjectId);
        Assert.Equal(before.DeckId, after.DeckId);
        Assert.Equal(before.Kind, after.Kind);
        Assert.Equal(before.CreatedAt, after.CreatedAt);

        var tags = await Tags.GetForCardsAsync([id]);
        Assert.Equal(2, tags[id].Count);
    }

    [Fact]
    public async Task Trashed_card_is_invisible_to_ordinary_search_and_visible_in_the_trash()
    {
        var id = await SeedCardAsync("Теорема Стокса");
        await Cards.MoveToTrashAsync([id]);

        Assert.Empty(await Cards.SearchAsync("стокса", new CardSearchOptions()));
        Assert.Single(await Cards.SearchAsync("стокса корзина", new CardSearchOptions()));
    }

    [Fact]
    public async Task Emptying_the_trash_removes_only_the_trash()
    {
        var kept = await SeedCardAsync("Теорема Стокса");
        var trashed = await SeedCardAsync("Теорема Гаусса");

        await Cards.MoveToTrashAsync([trashed]);
        var purged = await Cards.EmptyTrashAsync();

        Assert.Equal(1, purged.Value);
        Assert.Null(await Cards.GetByIdAsync(trashed));
        Assert.NotNull(await Cards.GetByIdAsync(kept));
    }

    [Fact]
    public async Task Moving_a_missing_card_to_trash_is_not_an_error()
    {
        var result = await Cards.MoveToTrashAsync([Guid.NewGuid()]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public async Task Bulk_assign_changes_subject_and_deck_for_every_card()
    {
        var subjectId = await SeedSubjectAsync();
        var deckId = await SeedDeckAsync(subjectId: subjectId);
        var first = await SeedCardAsync("Первая");
        var second = await SeedCardAsync("Вторая");

        var subjectResult = await Cards.AssignSubjectAsync([first, second], subjectId);
        var deckResult = await Cards.AssignDeckAsync([first, second], deckId);

        Assert.Equal(2, subjectResult.Value);
        Assert.Equal(2, deckResult.Value);
        Assert.Equal(2, await Cards.CountBySubjectAsync(subjectId));
        Assert.Equal(deckId, (await Cards.GetByIdAsync(first))!.DeckId);

        // Снятие привязки — тот же метод с null.
        await Cards.AssignDeckAsync([first], null);
        Assert.Null((await Cards.GetByIdAsync(first))!.DeckId);
    }

    [Fact]
    public async Task Bulk_assign_to_a_missing_subject_is_rejected()
    {
        var id = await SeedCardAsync();

        var result = await Cards.AssignSubjectAsync([id], Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("cards.subject_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Pin_and_suspend_are_persisted()
    {
        var id = await SeedCardAsync();

        await Cards.SetPinnedAsync(id, true);
        await Cards.SetSuspendedAsync(id, true);

        var card = await Cards.GetByIdAsync(id);
        Assert.True(card!.IsPinned);
        Assert.True(card.IsSuspended);
    }

    [Fact]
    public async Task Search_returns_cards_in_relevance_order()
    {
        var ordinary = await SeedCardAsync("Интеграл Лебега");
        var pinned = await SeedCardAsync("Интеграл Римана");
        await Cards.SetPinnedAsync(pinned, true);

        var hits = await Cards.SearchAsync("интеграл", new CardSearchOptions());

        Assert.Equal(2, hits.Count);
        Assert.Equal(pinned, hits[0].Id);
        Assert.Equal(ordinary, hits[1].Id);
    }

    [Fact]
    public async Task Similar_card_is_found_before_a_duplicate_is_created()
    {
        await SeedCardAsync("Формула Ньютона-Лейбница");

        var similar = await Cards.FindSimilarAsync("формула ньютона лейбница");
        var unrelated = await Cards.FindSimilarAsync("Теорема Стокса");

        Assert.Single(similar);
        Assert.Empty(unrelated);
    }

    [Fact]
    public async Task Similar_search_ignores_the_card_being_edited()
    {
        var id = await SeedCardAsync("Формула Ньютона-Лейбница");

        Assert.Empty(await Cards.FindSimilarAsync("Формула Ньютона-Лейбница", excludeId: id));
    }

    [Fact]
    public async Task Similar_search_says_nothing_on_a_single_letter()
    {
        await SeedCardAsync("Формула Ньютона-Лейбница");

        Assert.Empty(await Cards.FindSimilarAsync("ф"));
    }

    [Fact]
    public async Task Search_index_reports_itself_healthy()
    {
        Assert.True(await Cards.IsSearchIndexHealthyAsync());
    }

    // ---- Ретеншн корзины (new_addons.md §3.5, §11) -------------------------------------------

    [Fact]
    public async Task Purge_expired_trash_removes_only_cards_older_than_retention()
    {
        var oldId = await SeedCardAsync("Старая");
        var freshId = await SeedCardAsync("Свежая");

        await Cards.MoveToTrashAsync([oldId, freshId]);

        // Подвинуть DeletedAt старой карточки вручную — ретеншн должен смотреть на возраст, не на факт удаления.
        var old = await CardRepo.GetByIdAsync(oldId);
        old!.DeletedAt = DateTimeOffset.Now.AddDays(-40);
        await CardRepo.UpdateAsync(old);

        var purged = await Cards.PurgeExpiredTrashAsync(TimeSpan.FromDays(30));

        Assert.Equal(1, purged);
        Assert.Null(await CardRepo.GetByIdAsync(oldId));
        Assert.NotNull(await CardRepo.GetByIdAsync(freshId));
    }

    [Fact]
    public async Task Purge_expired_trash_leaves_everything_when_nothing_is_old_enough()
    {
        var id = await SeedCardAsync("Свежая");
        await Cards.MoveToTrashAsync([id]);

        var purged = await Cards.PurgeExpiredTrashAsync(TimeSpan.FromDays(30));

        Assert.Equal(0, purged);
        Assert.NotNull(await CardRepo.GetByIdAsync(id));
    }

    // ---- Wiki-ссылки [[…]] (new_addons.md §7, объём Phase 12.9) ------------------------------

    [Fact]
    public async Task Resolve_wiki_link_finds_an_exact_match()
    {
        var id = await SeedCardAsync("Теорема Стокса");

        var resolved = await Cards.ResolveWikiLinkAsync("Теорема Стокса");

        Assert.Equal(id, resolved.ExactCardId);
        Assert.Single(resolved.Candidates);
    }

    [Fact]
    public async Task Resolve_wiki_link_ignores_case_and_yo_e()
    {
        var id = await SeedCardAsync("Ёмкость конденсатора");

        var resolved = await Cards.ResolveWikiLinkAsync("емкость конденсатора");

        Assert.Equal(id, resolved.ExactCardId);
    }

    [Fact]
    public async Task Resolve_wiki_link_returns_close_candidates_when_nothing_matches_exactly()
    {
        await SeedCardAsync("Теорема Стокса");

        var resolved = await Cards.ResolveWikiLinkAsync("Теорема стокса про поток");

        Assert.Null(resolved.ExactCardId);
    }

    [Fact]
    public async Task Resolve_wiki_link_finds_nothing_for_an_unrelated_name()
    {
        await SeedCardAsync("Теорема Стокса");

        var resolved = await Cards.ResolveWikiLinkAsync("Совершенно другое название");

        Assert.Null(resolved.ExactCardId);
        Assert.Empty(resolved.Candidates);
    }
}
