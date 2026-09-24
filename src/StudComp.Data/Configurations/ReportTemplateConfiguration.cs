using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StudComp.Core.Domain;

namespace StudComp.Data.Configurations;

internal sealed class ReportTemplateConfiguration : IEntityTypeConfiguration<ReportTemplate>
{
    public void Configure(EntityTypeBuilder<ReportTemplate> builder)
    {
        builder.ToTable("ReportTemplates");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.GostVariant).HasMaxLength(50);
        // StyleProfileJson без лимита — это сериализованный GostStyleProfile (ARCHITECTURE §10.4).
        builder.Property(x => x.StyleProfileJson).IsRequired();
    }
}
