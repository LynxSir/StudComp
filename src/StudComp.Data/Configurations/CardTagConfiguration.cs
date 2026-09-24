using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация меток (new_addons.md §3.1). Метки глобальные, к предмету не привязаны.
/// </summary>
/// <remarks>
/// Уникальный индекс по нормализованному имени — первый <c>IsUnique</c> в проекте: у метки нет
/// другой идентичности, кроме её имени, и две строки «формулы» означали бы разъехавшиеся счётчики
/// и дубли в облаке меток. Сервис меток на это опирается: «найти или создать» ищет ровно по нему.
/// </remarks>
internal sealed class CardTagConfiguration : IEntityTypeConfiguration<CardTag>
{
    public void Configure(EntityTypeBuilder<CardTag> builder)
    {
        builder.ToTable("CardTags");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ColorHex).HasMaxLength(16);

        builder.Property(x => x.CreatedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);

        builder.HasIndex(x => x.Name).IsUnique();

        // Облако меток и автодополнение сортируются по частоте.
        builder.HasIndex(x => x.UsageCount);
    }
}
