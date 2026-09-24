using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class ScheduleEntryConfiguration : IEntityTypeConfiguration<ScheduleEntry>
{
    public void Configure(EntityTypeBuilder<ScheduleEntry> builder)
    {
        builder.ToTable("ScheduleEntries");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Room).HasMaxLength(100);
        builder.Property(x => x.Teacher).HasMaxLength(200);

        // Пара без предмета бессмысленна — удаление предмета уносит расписание (ARCHITECTURE §7.2).
        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.SubjectId);
    }
}
