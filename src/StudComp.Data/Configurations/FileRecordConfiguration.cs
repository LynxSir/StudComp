using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class FileRecordConfiguration : IEntityTypeConfiguration<FileRecord>
{
    public void Configure(EntityTypeBuilder<FileRecord> builder)
    {
        builder.ToTable("FileRecords");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OriginalPath).IsRequired().HasMaxLength(1024);
        builder.Property(x => x.CurrentPath).HasMaxLength(1024);
        builder.Property(x => x.ContentHash).HasMaxLength(128);
        builder.Property(x => x.DetectedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);

        // SubjectId nullable (до сортировки). Файл никогда не теряем (ARCHITECTURE §14) —
        // удаление предмета лишь отвязывает запись, не удаляет её.
        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.ContentHash);
        builder.HasIndex(x => x.Status);

        // Горячий путь reconciliation: точечный поиск по пути и выборка по префиксу папки
        // (ARCHITECTURE §8.2, Phase 10) — без индекса это полное сканирование на каждый файл.
        builder.HasIndex(x => x.OriginalPath);
    }
}
