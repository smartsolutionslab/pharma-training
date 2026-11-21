using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence.Configurations;

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
