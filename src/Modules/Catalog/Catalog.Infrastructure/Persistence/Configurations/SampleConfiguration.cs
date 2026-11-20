using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.Samples;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
    public void Configure(EntityTypeBuilder<Sample> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("samples");
        builder.HasKey(sample => sample.Id);
        builder.Property(sample => sample.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(sample => sample.Name).HasColumnName("name").HasMaxLength(Sample.MaxNameLength).IsRequired();
    }
}
