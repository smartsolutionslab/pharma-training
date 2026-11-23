using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

public sealed class CourseVersionChapterCopyConfiguration : IEntityTypeConfiguration<CourseVersionChapterCopy>
{
    private const int KindLength = 16;

    public void Configure(EntityTypeBuilder<CourseVersionChapterCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_version_chapters");
        // A chapter's id may repeat in the next version of its course.
        builder.HasKey(chapter => new { chapter.CourseVersionId, chapter.ChapterId }).HasName("pk_course_version_chapters");
        builder.Property(chapter => chapter.CourseVersionId).HasColumnName("course_version_id").ValueGeneratedNever();
        builder.Property(chapter => chapter.ChapterId).HasColumnName("chapter_id").ValueGeneratedNever();
        builder.Property(chapter => chapter.Position).HasColumnName("position").IsRequired();
        builder.Property(chapter => chapter.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(KindLength).IsRequired();
        builder.Property(chapter => chapter.VideoDurationSeconds).HasColumnName("video_duration_seconds");
    }
}
