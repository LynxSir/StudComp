using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

/// <summary>
/// EF-конфигурация учебного семестра (ARCHITECTURE §7.1 <c>SEMESTER</c>). <c>DateOnly</c> провайдер
/// SQLite кладёт в <c>TEXT</c> сам — свой конвертер не нужен (как уже работающий <c>TimeOnly</c>
/// в расписании).
/// </summary>
internal sealed class SemesterConfiguration : IEntityTypeConfiguration<Semester>
{
    public void Configure(EntityTypeBuilder<Semester> builder)
    {
        builder.ToTable("Semesters");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        // Сетка звонков — value-object целиком внутри семестра, отдельной таблицы не заводим
        // (прецедент JSON-колонки — ReportTemplate.StyleProfileJson). Лимита длины нет.
        builder.Property(x => x.PairSlotsJson).IsRequired();

        // Активный семестр ровно один, и он же — самый горячий запрос (GetActiveAsync).
        builder.HasIndex(x => x.IsActive);
    }
}
