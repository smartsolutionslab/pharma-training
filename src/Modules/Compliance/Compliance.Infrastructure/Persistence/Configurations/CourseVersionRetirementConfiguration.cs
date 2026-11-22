using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence.Configurations;

public sealed class CourseVersionRetirementConfiguration : IEntityTypeConfiguration<CourseVersionRetirement>
{
    public void Configure(EntityTypeBuilder<CourseVersionRetirement> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign key to the version: a retirement may arrive before the version it retires.
        builder.ToTable("course_version_retirements");
        builder.HasKey(retirement => retirement.Id).HasName("pk_course_version_retirements");
        builder.Property(retirement => retirement.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(retirement => retirement.Course).HasColumnName("course_id").IsRequired();

        builder.HasIndex(retirement => retirement.Course).HasDatabaseName("ix_course_version_retirements_course_id");
    }
}
