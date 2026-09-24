using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Метки (new_addons.md §3.1). Главное здесь — нормализация: «#Формулы», «формулы» и «ФОРМУЛЫ»
/// обязаны быть одной меткой, иначе облако меток за месяц превратится в свалку синонимов.
/// </summary>
public sealed class CardTagServiceTests : CardsDatabaseTestBase
{
    [Theory]
    [InlineData("формулы", "формулы")]
    [InlineData("Формулы", "формулы")]
    [InlineData("#Формулы", "формулы")]
    [InlineData("  #ФОРМУЛЫ  ", "формулы")]
    [InlineData("Ёмкость", "емкость")]
    public void Normalization_folds_case_hash_and_yo(string input, string expected)
    {
        Assert.Equal(expected, ICardTagService.Normalize(input));
    }

    [Fact]
    public async Task Ensure_creates_once_and_finds_afterwards()
    {
        var first = await Tags.EnsureAsync("#Формулы");
        var second = await Tags.EnsureAsync("формулы");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Single(await Tags.GetAllAsync());

        // Отображаемое имя сохраняет то, как метку написал человек.
        Assert.Equal("Формулы", first.Value.DisplayName);
        Assert.Equal("формулы", first.Value.Name);
    }

    [Fact]
    public async Task Ensure_rejects_an_empty_name()
    {
        var result = await Tags.EnsureAsync("  #  ");

        Assert.True(result.IsFailure);
        Assert.Equal("cards.tag_name_required", result.Error.Code);
    }

    [Fact]
    public async Task Ensure_many_skips_blanks_and_duplicates()
    {
        var tags = await Tags.EnsureManyAsync(["формулы", "Формулы", "", "   ", "теория"]);

        Assert.Equal(2, tags.Count);
        Assert.Equal(2, (await Tags.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Card_tags_are_returned_in_bulk()
    {
        var first = await SeedCardAsync("Первая", tags: ["формулы", "теория"]);
        var second = await SeedCardAsync("Вторая", tags: ["формулы"]);

        var map = await Tags.GetForCardsAsync([first, second]);

        Assert.Equal(2, map[first].Count);
        Assert.Single(map[second]);
    }

    [Fact]
    public async Task Replacing_card_tags_removes_the_dropped_ones()
    {
        var id = await SeedCardAsync("Первая", tags: ["формулы", "теория"]);

        var card = await Cards.GetByIdAsync(id);
        await Cards.UpdateAsync(card!, ["теория"]);

        var map = await Tags.GetForCardsAsync([id]);
        Assert.Equal("теория", Assert.Single(map[id]).Name);
    }

    [Fact]
    public async Task Usage_count_follows_the_actual_links()
    {
        await SeedCardAsync("Первая", tags: ["формулы"]);
        await SeedCardAsync("Вторая", tags: ["формулы"]);
        await SeedCardAsync("Третья", tags: ["теория"]);

        var top = await Tags.GetTopAsync(10);

        Assert.Equal("формулы", top[0].Name);
        Assert.Equal(2, top[0].UsageCount);
        Assert.Equal(1, top[1].UsageCount);
    }

    [Fact]
    public async Task Merge_moves_every_card_and_drops_the_source_tag()
    {
        var onlyOld = await SeedCardAsync("Первая", tags: ["матан"]);
        var both = await SeedCardAsync("Вторая", tags: ["матан", "анализ"]);

        var from = (await Tags.EnsureAsync("матан")).Value;
        var to = (await Tags.EnsureAsync("анализ")).Value;

        var result = await Tags.MergeAsync(from.Id, to.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("анализ", Assert.Single(await Tags.GetAllAsync()).Name);

        var map = await Tags.GetForCardsAsync([onlyOld, both]);
        Assert.Equal(to.Id, Assert.Single(map[onlyOld]).Id);

        // У карточки, где обе метки уже были, дубля не появилось — составной ключ бы и не дал.
        Assert.Equal(to.Id, Assert.Single(map[both]).Id);
        Assert.Equal(2, (await Tags.GetTopAsync(10))[0].UsageCount);
    }

    [Fact]
    public async Task Merge_into_itself_is_rejected()
    {
        var tag = (await Tags.EnsureAsync("формулы")).Value;

        var result = await Tags.MergeAsync(tag.Id, tag.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("cards.tag_merge_into_itself", result.Error.Code);
    }

    [Fact]
    public async Task Merge_with_a_missing_tag_is_rejected()
    {
        var tag = (await Tags.EnsureAsync("формулы")).Value;

        var result = await Tags.MergeAsync(tag.Id, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("cards.tag_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Rename_refuses_to_collide_with_an_existing_tag()
    {
        var tag = (await Tags.EnsureAsync("матан")).Value;
        await Tags.EnsureAsync("анализ");

        var result = await Tags.RenameAsync(tag.Id, "Анализ");

        Assert.True(result.IsFailure);
        Assert.Equal("cards.tag_already_exists", result.Error.Code);
    }

    [Fact]
    public async Task Rename_updates_both_names()
    {
        var tag = (await Tags.EnsureAsync("матан")).Value;

        var result = await Tags.RenameAsync(tag.Id, "#Мат. Анализ");

        Assert.True(result.IsSuccess);
        var renamed = Assert.Single(await Tags.GetAllAsync());
        Assert.Equal("мат. анализ", renamed.Name);
        Assert.Equal("Мат. Анализ", renamed.DisplayName);
    }

    [Fact]
    public async Task Deleting_a_tag_keeps_the_cards()
    {
        var id = await SeedCardAsync("Первая", tags: ["формулы"]);
        var tag = (await Tags.EnsureAsync("формулы")).Value;

        var result = await Tags.DeleteAsync(tag.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(await Cards.GetByIdAsync(id));
        Assert.Empty(await Tags.GetAllAsync());
    }

    [Fact]
    public async Task Tagged_card_is_findable_by_its_tag()
    {
        var id = await SeedCardAsync("Теорема Стокса", tags: ["формулы"]);

        var hits = await Cards.SearchAsync("#формулы", new CardSearchOptions());

        Assert.Equal(id, Assert.Single(hits).Id);
    }
}
