using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class CourseVersionConfiguration : IEntityTypeConfiguration<CourseVersion>
{
    public void Configure(EntityTypeBuilder<CourseVersion> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_versions");
        builder.HasKey(version => version.Id).HasName("pk_course_versions");
        builder.Property(version => version.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(version => version.CourseId).HasColumnName("course_id").IsRequired();
        builder.Property(version => version.VersionNumber).HasColumnName("version_number").IsRequired();
        // Whatever Editorial accepted, without a length of the catalog's own.
        builder.Property(version => version.Title).HasColumnName("title").HasColumnType("text").IsRequired();
        builder.Property(version => version.ValidityMonths).HasColumnName("validity_months").IsRequired();

        // One version number per course; it also backs the search for a course's higher version.
        builder.HasIndex(version => new { version.CourseId, version.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ix_course_versions_course_id_version_number");

        builder.HasMany(version => version.Chapters)
            .WithOne()
            .HasForeignKey(chapter => chapter.CourseVersionId)
            .HasConstraintName("fk_course_chapters_course_versions_course_version_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
