using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class CourseChapterConfiguration : IEntityTypeConfiguration<CourseChapter>
{
    private const int KindLength = 16;

    public void Configure(EntityTypeBuilder<CourseChapter> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_chapters");
        // A chapter's id may repeat in the next version of its course.
        builder.HasKey(chapter => new { chapter.CourseVersionId, chapter.ChapterId }).HasName("pk_course_chapters");
        builder.Property(chapter => chapter.CourseVersionId).HasColumnName("course_version_id").ValueGeneratedNever();
        builder.Property(chapter => chapter.ChapterId).HasColumnName("chapter_id").ValueGeneratedNever();
        builder.Property(chapter => chapter.Position).HasColumnName("position").IsRequired();
        builder.Property(chapter => chapter.Title).HasColumnName("title").HasColumnType("text").IsRequired();
        builder.Property(chapter => chapter.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(KindLength).IsRequired();
        builder.Property(chapter => chapter.VideoDurationSeconds).HasColumnName("video_duration_seconds");
        builder.Property(chapter => chapter.Text).HasColumnName("text").HasColumnType("text");
        builder.Property(chapter => chapter.File).HasColumnName("file").HasColumnType("text");
    }
}
