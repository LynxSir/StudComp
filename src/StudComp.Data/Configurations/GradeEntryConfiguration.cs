using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class GradeEntryConfiguration : IEntityTypeConfiguration<GradeEntry>
{
    public void Configure(EntityTypeBuilder<GradeEntry> builder)
    {
        builder.ToTable("GradeEntries");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.RawScore).HasConversion(DataConverters.DecimalToInvariantString);
        builder.Property(x => x.MaxScore).HasConversion(DataConverters.DecimalToInvariantString);
        builder.Property(x => x.Weight).HasConversion(DataConverters.DecimalToInvariantString);

        // Имя строки зачётки и признак «ещё не сдано» (ARCHITECTURE §9.4).
        builder.Property(x => x.Title).HasMaxLength(200);

        // Оценка без предмета бессмысленна — удаление предмета уносит зачётку (ARCHITECTURE §7.2).
        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.SubjectId);
    }
}
