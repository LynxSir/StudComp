using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация связки «карточка ↔ метка» (new_addons.md §3.1, §3.3).
/// </summary>
/// <remarks>
/// Первый составной первичный ключ в проекте: у чистой join-таблицы нет собственной идентичности,
/// а пара «карточка + метка» и так уникальна. Обе стороны — <c>CASCADE</c>: без карточки или без
/// метки связка бессмысленна, это служебная строка, а не пользовательский текст.
/// </remarks>
internal sealed class CardTagLinkConfiguration : IEntityTypeConfiguration<CardTagLink>
{
    public void Configure(EntityTypeBuilder<CardTagLink> builder)
    {
        builder.ToTable("CardTagLinks");

        builder.HasKey(x => new { x.CardId, x.TagId });

        builder.HasOne<Card>()
            .WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CardTag>()
            .WithMany()
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // Обратный поиск «все карточки этой метки». По CardId индекс не нужен — это ведущая
        // колонка составного ключа.
        builder.HasIndex(x => x.TagId);
    }
}
