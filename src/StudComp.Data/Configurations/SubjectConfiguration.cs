using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("Subjects");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Code).HasMaxLength(50);
        builder.Property(x => x.TeacherFullName).HasMaxLength(200);
        builder.Property(x => x.FolderPath).HasMaxLength(1024);
        builder.Property(x => x.ColorHex).HasMaxLength(9);

        // Система оценивания предмета (ARCHITECTURE §9.4). Kind EF пишет как int сам; произвольные
        // границы — через строковый конвертер, как и прочие decimal (в БД по ним не сравниваем).
        builder.Property(x => x.GradeScaleMax).HasConversion(DataConverters.NullableDecimalToInvariantString);
        builder.Property(x => x.GradeScalePassThreshold).HasConversion(DataConverters.NullableDecimalToInvariantString);
        builder.Property(x => x.ForecastStrategyName).HasMaxLength(64);

        // Семестр предмета (new_addons.md §5). Удаление семестра лишь обнуляет ссылку — предмет,
        // его оценки и файлы переживают чистку старых семестров (ARCHITECTURE §7.2, §14 по духу).
        builder.HasOne<Semester>()
            .WithMany()
            .HasForeignKey(x => x.SemesterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.SemesterId);

        // Профиль оформления отчётов предмета (ARCHITECTURE §10.4). Удаление шаблона лишь обнуляет
        // ссылку — предмет и его отчёты остаются, просто вернутся к заводскому профилю.
        builder.HasOne<ReportTemplate>()
            .WithMany()
            .HasForeignKey(x => x.ReportTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ReportTemplateId);
    }
}
