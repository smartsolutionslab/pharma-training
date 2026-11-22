using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence.Configurations;

public sealed class CourseVersionConfiguration : IEntityTypeConfiguration<CourseVersion>
{
    public void Configure(EntityTypeBuilder<CourseVersion> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_versions");
        builder.HasKey(version => version.Id).HasName("pk_course_versions");
        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(version => version.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(version => version.Course).HasColumnName("course_id").IsRequired();
        builder.Property(version => version.Number).HasColumnName("version_number").IsRequired();
        // As long as Editorial allows a course title to be.
        builder.Property(version => version.Title).HasColumnName("title").HasMaxLength(CourseTitle.MaxLength).IsRequired();
        builder.Property(version => version.Validity).HasColumnName("validity_months").IsRequired();

        // One version number per course; it also backs reading a course's versions.
        builder.HasIndex(version => new { version.Course, version.Number })
            .IsUnique()
            .HasDatabaseName("ix_course_versions_course_id_version_number");
    }
}
