using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация колод карточек (new_addons.md §3.1, §3.3).
/// </summary>
/// <remarks>
/// Предмет — <c>SET NULL</c>: удалили предмет, колода осталась. Карточки колоды при её удалении
/// тоже не пропадают — за это отвечает <c>SET NULL</c> на <c>Card.DeckId</c>.
/// </remarks>
internal sealed class CardDeckConfiguration : IEntityTypeConfiguration<CardDeck>
{
    public void Configure(EntityTypeBuilder<CardDeck> builder)
    {
        builder.ToTable("CardDecks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ColorHex).HasMaxLength(16);
        builder.Property(x => x.QueryExpression).HasMaxLength(500);

        builder.Property(x => x.CreatedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);
        builder.Property(x => x.UpdatedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        // Горячий путь рельса фильтров — «колоды этого предмета».
        builder.HasIndex(x => x.SubjectId);
    }
}
