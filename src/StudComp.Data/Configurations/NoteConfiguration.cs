using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация заметок (ARCHITECTURE §7.1 <c>NOTE</c>, new_addons.md §1.9).
/// </summary>
/// <remarks>
/// Все внешние ключи — <c>SET NULL</c>, включая предмет: заметка это написанный руками текст,
/// и удаление предмета, файла или пары не должно его уносить. Это осознанно строже, чем каскад у
/// расписания и оценок (ARCHITECTURE §7.2), и следует духу §14. Единственное исключение —
/// <c>DeadlineId</c>: служебные заметки задания/ответа принадлежат дедлайну и уходят вместе с ним
/// (удаление дедлайна подтверждается диалогом, а <c>.md</c>-копии на диске остаются).
/// </remarks>
internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).IsRequired().HasMaxLength(300);

        // Тело заметки без лимита длины — как StyleProfileJson у шаблона отчёта.
        builder.Property(x => x.ContentMarkdown).IsRequired();

        builder.Property(x => x.LinkedPath).HasMaxLength(1024);

        builder.Property(x => x.CreatedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);
        builder.Property(x => x.UpdatedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(x => x.LinkedFileRecordId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<ScheduleEntry>()
            .WithMany()
            .HasForeignKey(x => x.ScheduleEntryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Deadline>()
            .WithMany()
            .HasForeignKey(x => x.DeadlineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.LinkedFileRecordId);
        builder.HasIndex(x => x.ScheduleEntryId);
        builder.HasIndex(x => x.DeadlineId);

        // Горячий путь Дашборда и вкладки «Заметки» — «свежие сверху».
        builder.HasIndex(x => x.UpdatedAt);
        builder.HasIndex(x => x.LinkedPath);
    }
}
