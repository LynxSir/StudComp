using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация сессий работы с карточками (new_addons.md §3.1, §3.3).
/// </summary>
/// <remarks>
/// Предмет и колода — <c>SET NULL</c>: историю тренировок не теряем даже после удаления того, по
/// чему тренировались.
/// </remarks>
internal sealed class StudySessionConfiguration : IEntityTypeConfiguration<StudySession>
{
    public void Configure(EntityTypeBuilder<StudySession> builder)
    {
        builder.ToTable("StudySessions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Снимок фильтра без лимита длины — колонка-значение, как PairSlotsJson у семестра.
        builder.Property(x => x.FilterJson).IsRequired();

        builder.Property(x => x.StartedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);
        builder.Property(x => x.FinishedAt).HasConversion(DataConverters.NullableDateTimeOffsetToUtc);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<CardDeck>()
            .WithMany()
            .HasForeignKey(x => x.DeckId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.DeckId);

        // Вкладка «Статистика» читает сессии в обратном хронологическом порядке.
        builder.HasIndex(x => x.StartedAt);
    }
}
