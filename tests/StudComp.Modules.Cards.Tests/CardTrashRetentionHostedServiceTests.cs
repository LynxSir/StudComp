using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Ретеншн «Корзины» карточек (new_addons.md §3.5, §11, Phase 12.9): раз в сутки вычищает то, что
/// удалено раньше <see cref="DataOptions.CardTrashRetentionDays"/> назад.
/// </summary>
public sealed class CardTrashRetentionHostedServiceTests : CardsDatabaseTestBase
{
    private CardTrashRetentionHostedService CreateService(int retentionDays) =>
        new(
            Cards,
            new TestOptionsMonitor<DataOptions>(new DataOptions { CardTrashRetentionDays = retentionDays }),
            NullLogger<CardTrashRetentionHostedService>.Instance);

    [Fact]
    public async Task Cards_deleted_longer_ago_than_the_retention_are_purged()
    {
        var oldId = await SeedCardAsync("Старая");
        var freshId = await SeedCardAsync("Свежая");
        await Cards.MoveToTrashAsync([oldId, freshId]);

        var old = await CardRepo.GetByIdAsync(oldId);
        old!.DeletedAt = DateTimeOffset.Now.AddDays(-40);
        await CardRepo.UpdateAsync(old);

        var service = CreateService(retentionDays: 30);
        var purged = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.Null(await CardRepo.GetByIdAsync(oldId));
        Assert.NotNull(await CardRepo.GetByIdAsync(freshId));
    }

    [Fact]
    public async Task Nothing_old_enough_leaves_the_trash_untouched()
    {
        var id = await SeedCardAsync("Свежая");
        await Cards.MoveToTrashAsync([id]);

        var service = CreateService(retentionDays: 30);
        var purged = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, purged);
        Assert.NotNull(await CardRepo.GetByIdAsync(id));
    }

    [Fact]
    public async Task A_non_positive_retention_disables_purging_entirely()
    {
        var id = await SeedCardAsync("Старая");
        await Cards.MoveToTrashAsync([id]);
        var card = await CardRepo.GetByIdAsync(id);
        card!.DeletedAt = DateTimeOffset.Now.AddDays(-400);
        await CardRepo.UpdateAsync(card);

        var service = CreateService(retentionDays: 0);
        var purged = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, purged);
        Assert.NotNull(await CardRepo.GetByIdAsync(id));
    }

    [Fact]
    public async Task Live_cards_are_never_touched_by_retention()
    {
        var id = await SeedCardAsync("Живая");

        var service = CreateService(retentionDays: 1);
        var purged = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, purged);
        Assert.NotNull(await CardRepo.GetByIdAsync(id));
    }
}
