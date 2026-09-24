using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class DeadlineConfiguration : IEntityTypeConfiguration<Deadline>
{
    public void Configure(EntityTypeBuilder<Deadline> builder)
    {
        builder.ToTable("Deadlines");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).IsRequired().HasMaxLength(300);
        builder.Property(x => x.DueDate).HasConversion(DataConverters.DateTimeOffsetToUtc);
        builder.Property(x => x.FolderName).HasMaxLength(255);
        builder.Property(x => x.AnsweredAt).HasConversion(DataConverters.NullableDateTimeOffsetToUtc);

        // Дедлайн без предмета бессмысленен — удаление предмета уносит дедлайны (ARCHITECTURE §7.2).
        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Удаление привязанного файла не должно сносить дедлайн — только отвязать (ARCHITECTURE §9.3).
        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(x => x.LinkedFileRecordId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.LinkedFileRecordId);
        builder.HasIndex(x => x.DueDate);
        builder.HasIndex(x => x.Status);
    }
}
