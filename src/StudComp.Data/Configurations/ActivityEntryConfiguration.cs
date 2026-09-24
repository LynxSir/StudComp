using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация ленты активности (ARCHITECTURE §7.1 <c>ACTIVITY_LOG</c>). Источник данных для
/// Дашборда; ретеншн-прунинг делает <c>ActivityLogListener</c> в App-слое.
/// </summary>
internal sealed class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> builder)
    {
        builder.ToTable("ActivityLog");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Timestamp).HasConversion(DataConverters.DateTimeOffsetToUtc);
        builder.Property(x => x.Path).HasMaxLength(1024);
        builder.Property(x => x.Title).HasMaxLength(400);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        // Горячий путь Дашборда — «свежие сверху»; фильтр по виду события — вторичный.
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.Kind);
        builder.HasIndex(x => x.SubjectId);
    }
}
