using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Samples;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

/// <summary>One row per sample, written after its events. The samples themselves are their event streams.</summary>
public sealed class SampleSummaryConfiguration : IEntityTypeConfiguration<SampleSummary>
{
    public void Configure(EntityTypeBuilder<SampleSummary> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("sample_summaries");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(SampleName.MaxLength).IsRequired();
    }
}
