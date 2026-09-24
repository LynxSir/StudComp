using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class FileOperationLogEntryConfiguration : IEntityTypeConfiguration<FileOperationLogEntry>
{
    public void Configure(EntityTypeBuilder<FileOperationLogEntry> builder)
    {
        builder.ToTable("FileOperationLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OriginalPath).IsRequired().HasMaxLength(1024);
        builder.Property(x => x.PlannedPath).IsRequired().HasMaxLength(1024);
        builder.Property(x => x.FinalPath).HasMaxLength(1024);
        builder.Property(x => x.StartedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);
        builder.Property(x => x.CompletedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);

        // Журнал имеет смысл только вместе со своей записью учёта — уходит каскадом.
        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(x => x.FileRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // Правило может быть удалено пользователем — журнал прошлых операций от этого не страдает.
        builder.HasOne<ArchivistRule>()
            .WithMany()
            .HasForeignKey(x => x.RuleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.FileRecordId);
        builder.HasIndex(x => x.RuleId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.StartedAt);
    }
}
