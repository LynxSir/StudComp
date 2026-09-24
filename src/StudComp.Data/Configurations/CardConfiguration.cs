using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация карточек знаний (new_addons.md §3.1, §3.3, §3.4).
/// </summary>
/// <remarks>
/// Все четыре внешних ключа — <c>SET NULL</c>, включая предмет: карточка это написанный руками
/// текст, и удаление предмета, колоды, заметки или файла не должно его уносить. Ровно та же логика,
/// что у <c>NoteConfiguration</c>, и то же следование духу §14.
/// </remarks>
internal sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Cards");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Front).IsRequired().HasMaxLength(500);

        // Оборот без лимита длины — там Markdown с листингами и таблицами, как ContentMarkdown заметки.
        builder.Property(x => x.Back).IsRequired();

        builder.Property(x => x.Hint).HasMaxLength(1000);
        builder.Property(x => x.Source).HasMaxLength(300);
        builder.Property(x => x.SchedulerName).IsRequired().HasMaxLength(100);

        builder.Property(x => x.CreatedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);
        builder.Property(x => x.UpdatedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);
        builder.Property(x => x.DueAt).HasConversion(DataConverters.NullableDateTimeOffsetToUtc);
        builder.Property(x => x.DeletedAt).HasConversion(DataConverters.NullableDateTimeOffsetToUtc);
        builder.Property(x => x.LastReviewedAt).HasConversion(DataConverters.NullableDateTimeOffsetToUtc);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<CardDeck>()
            .WithMany()
            .HasForeignKey(x => x.DeckId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Note>()
            .WithMany()
            .HasForeignKey(x => x.SourceNoteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(x => x.SourceFileRecordId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.DeckId);
        builder.HasIndex(x => x.SourceNoteId);
        builder.HasIndex(x => x.SourceFileRecordId);

        // Горячий путь библиотеки — «свежие сверху».
        builder.HasIndex(x => x.UpdatedAt);

        // Горячий путь очереди повторения и «Корзины».
        builder.HasIndex(x => x.DueAt);
        builder.HasIndex(x => x.DeletedAt);

        // Очередь дня всегда спрашивает «не удалённые, у которых подошёл срок» — составной индекс
        // закрывает оба условия одним проходом (new_addons.md §3.4).
        builder.HasIndex(x => new { x.DeletedAt, x.DueAt });
    }
}
