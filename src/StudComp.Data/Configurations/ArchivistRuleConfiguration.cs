using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class ArchivistRuleConfiguration : IEntityTypeConfiguration<ArchivistRule>
{
    public void Configure(EntityTypeBuilder<ArchivistRule> builder)
    {
        builder.ToTable("ArchivistRules");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Pattern).IsRequired().HasMaxLength(500);
        builder.Property(x => x.RenameTemplate).HasMaxLength(500);
        builder.Property(x => x.WorkType).HasMaxLength(100);

        // NULL = «во всех наблюдаемых папках»; отдельный индекс не нужен — правил десятки,
        // фильтрация идёт в памяти движка (ADR §16.54).
        builder.Property(x => x.WatchedFolder).HasMaxLength(1024);

        // SubjectId nullable: правило может быть общим — при удалении предмета оно таким и становится,
        // а не пропадает (ARCHITECTURE §8.4 п.3).
        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.Priority);
    }
}
