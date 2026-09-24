using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация журнала ответов (new_addons.md §3.1, §3.3).
/// </summary>
/// <remarks>
/// Карточка — <c>CASCADE</c>: журнал ответов по несуществующей карточке бессмыслен и статистику
/// только испортит. Сессия — <c>SET NULL</c>: историю тренировок теряем неохотно, а сам факт ответа
/// остаётся полезным и без сессии.
/// </remarks>
internal sealed class CardReviewLogConfiguration : IEntityTypeConfiguration<CardReviewLog>
{
    public void Configure(EntityTypeBuilder<CardReviewLog> builder)
    {
        builder.ToTable("CardReviewLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ReviewedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);

        builder.HasOne<Card>()
            .WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<StudySession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.SetNull);

        // Горячие пути статистики: «история этой карточки», «что было за период», «итоги сессии».
        builder.HasIndex(x => x.CardId);
        builder.HasIndex(x => x.ReviewedAt);
        builder.HasIndex(x => x.SessionId);
    }
}
