using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Samples;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence.Configurations;

public sealed class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
    public void Configure(EntityTypeBuilder<Sample> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("samples");
        builder.HasKey(sample => sample.Id);
        // The identifier and the value object map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(sample => sample.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(sample => sample.Name).HasColumnName("name").HasMaxLength(SampleName.MaxLength).IsRequired();
    }
}
