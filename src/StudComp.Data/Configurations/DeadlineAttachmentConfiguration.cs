using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация вложений дедлайна. Запись — только учёт: файл живёт в папке предмета, и каскад
/// при удалении дедлайна уносит строки, а не файлы (ARCHITECTURE §14).
/// </summary>
internal sealed class DeadlineAttachmentConfiguration : IEntityTypeConfiguration<DeadlineAttachment>
{
    public void Configure(EntityTypeBuilder<DeadlineAttachment> builder)
    {
        builder.ToTable("DeadlineAttachments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileName).IsRequired().HasMaxLength(260);
        builder.Property(x => x.RelativePath).IsRequired().HasMaxLength(1024);
        builder.Property(x => x.AddedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);

        builder.HasOne<Deadline>()
            .WithMany()
            .HasForeignKey(x => x.DeadlineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DeadlineId);
    }
}
