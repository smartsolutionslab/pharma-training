using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

/// <summary>One row per course version Learning received; its primary key backs the consumer's check that a version is stored once.</summary>
public sealed class CourseVersionCopyConfiguration : IEntityTypeConfiguration<CourseVersionCopy>
{
    public void Configure(EntityTypeBuilder<CourseVersionCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_versions");
        builder.HasKey(version => version.Id).HasName("pk_course_versions");
        builder.Property(version => version.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(version => version.CourseId).HasColumnName("course_id").IsRequired();
        builder.Property(version => version.VersionNumber).HasColumnName("version_number").IsRequired();

        builder.HasMany(version => version.Chapters)
            .WithOne()
            .HasForeignKey(chapter => chapter.CourseVersionId)
            .HasConstraintName("fk_course_version_chapters_course_versions_course_version_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
