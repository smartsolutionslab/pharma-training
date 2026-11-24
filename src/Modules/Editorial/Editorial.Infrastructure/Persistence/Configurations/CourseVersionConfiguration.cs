using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence.Configurations;

public sealed class CourseVersionConfiguration : IEntityTypeConfiguration<CourseVersion>
{
    private const int EnumLength = 20;

    public void Configure(EntityTypeBuilder<CourseVersion> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_versions");
        builder.HasKey(version => version.Id);
        // The identifier and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(version => version.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(version => version.Course).HasColumnName("course_id").IsRequired();
        builder.Property(version => version.Number).HasColumnName("number").IsRequired();
        builder.Property(version => version.Title).HasColumnName("title").HasMaxLength(CourseTitle.MaxLength).IsRequired();
        builder.Property(version => version.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(version => version.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(version => version.SubmittedBy).HasColumnName("submitted_by");
        builder.Property(version => version.BlueprintChangedBy).HasColumnName("blueprint_changed_by");
        builder.Property(version => version.PublishedBy).HasColumnName("published_by");
        builder.Property(version => version.PublishedAt).HasColumnName("published_at");
        builder.Property(version => version.ReturnedBy).HasColumnName("returned_by");
        builder.Property(version => version.ReturnedAt).HasColumnName("returned_at");
        builder.Property(version => version.RetiredBy).HasColumnName("retired_by");
        builder.Property(version => version.RetiredAt).HasColumnName("retired_at");
        // The version is the consistency boundary: a save of it or of any of its chapters, questions or options checks and moves this on.
        builder.HasRowVersion();
        builder.ComplexProperty(version => version.Blueprint, blueprint =>
        {
            blueprint.Property(value => value.Questions).HasColumnName("blueprint_questions").IsRequired();
            blueprint.Property(value => value.PassingScore).HasColumnName("blueprint_passing_score").IsRequired();
            blueprint.Property(value => value.Validity).HasColumnName("blueprint_validity_months").IsRequired();
        });

        builder.HasMany(version => version.Chapters)
            .WithOne()
            .HasForeignKey(ChapterConfiguration.CourseVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(version => version.Chapters)
            .HasField("chapters")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(version => version.Questions)
            .WithOne()
            .HasForeignKey(QuestionConfiguration.CourseVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(version => version.Questions)
            .HasField("questions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // One number per course, backed twice: DraftNextVersionHandler refuses an existing number with
        // editorial.next-version-exists; a racing insert answers 409 already-exists. It also serves lookups by course
        // (its leading column).
        builder.HasIndex(version => new { version.Course, version.Number })
            .IsUnique()
            .HasDatabaseName("ix_course_versions_course_id_number");
        builder.HasIndex(version => version.Status);
    }
}
