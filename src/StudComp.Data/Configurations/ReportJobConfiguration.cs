using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class ReportJobConfiguration : IEntityTypeConfiguration<ReportJob>
{
    public void Configure(EntityTypeBuilder<ReportJob> builder)
    {
        builder.ToTable("ReportJobs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SourcePath).HasMaxLength(1024);
        builder.Property(x => x.OutputPath).HasMaxLength(1024);
        builder.Property(x => x.CreatedAt).HasConversion(DataConverters.DateTimeOffsetToUtc);

        // Не удалять шаблон, на который ссылается история задач (ARCHITECTURE §7.1).
        builder.HasOne<ReportTemplate>()
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        // Отчёт мог быть сгенерирован «без предмета» — удаление предмета лишь отвязывает задачу.
        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.TemplateId);
        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.CreatedAt);
    }
}
