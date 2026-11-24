using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence.Configurations;

public sealed class ChapterConfiguration : IEntityTypeConfiguration<Chapter>
{
    /// <summary>The shadow foreign key to the version a chapter belongs to; the Domain keeps it in the aggregate.</summary>
    internal const string CourseVersionId = "CourseVersionId";

    /// <summary>
    /// The chapter's place in its version, counted from 1: the order of the aggregate's list, which
    /// <see cref="EditorialDbContext"/> writes before every save. No unique index: a reorder would break one for a moment.
    /// </summary>
    internal const string Position = "Position";

    private const int EnumLength = 20;

    public void Configure(EntityTypeBuilder<Chapter> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_version_chapters");
        builder.HasKey(chapter => chapter.Id);
        // The identifier and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(chapter => chapter.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<CourseVersionId>(CourseVersionId).HasColumnName("course_version_id").IsRequired();
        builder.Property<int>(Position).HasColumnName("position").IsRequired();
        builder.Property(chapter => chapter.Title).HasColumnName("title").HasMaxLength(ChapterTitle.MaxLength).IsRequired();
        builder.Property(chapter => chapter.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        // Markdown is stored as written and never rendered here; MarkdownText caps its length.
        builder.Property(chapter => chapter.Text).HasColumnName("text").HasColumnType("text");
        builder.Property(chapter => chapter.File).HasColumnName("file").HasMaxLength(BlobReference.MaxLength);
        builder.Property(chapter => chapter.Duration).HasColumnName("duration_seconds");
        // A PDF's extraction (#157): null for text and video chapters, and for PDFs recorded before it existed.
        builder.Property(chapter => chapter.Extraction).HasColumnName("pdf_text_extraction").HasConversion<string>().HasMaxLength(EnumLength);
        builder.Property(chapter => chapter.ExtractedText).HasColumnName("pdf_text").HasColumnType("text");

        builder.HasIndex(CourseVersionId);

        // One file, one chapter of a version: CourseVersion refuses it, and this backs it (FileInUse). A text chapter has
        // no file, and PostgreSQL holds nulls distinct, so any number of them pass. A chapter copied into the next
        // version keeps its file under another version, so the rule is per version.
        builder.HasIndex(CourseVersionId, nameof(Chapter.File)).IsUnique();
    }
}
