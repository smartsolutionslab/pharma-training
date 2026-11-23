using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

/// <summary>One row per course version Editorial retired; its primary key backs the consumer's check that a retirement is stored once.</summary>
public sealed class CourseVersionRetirementConfiguration : IEntityTypeConfiguration<CourseVersionRetirement>
{
    public void Configure(EntityTypeBuilder<CourseVersionRetirement> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign key to the version: a retirement may arrive before the version it retires.
        builder.ToTable("course_version_retirements");
        builder.HasKey(retirement => retirement.CourseVersionId).HasName("pk_course_version_retirements");
        builder.Property(retirement => retirement.CourseVersionId).HasColumnName("course_version_id").ValueGeneratedNever();
        builder.Property(retirement => retirement.CourseId).HasColumnName("course_id").IsRequired();
    }
}
