using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence.Configurations;

public sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    /// <summary>The shadow foreign key to the version a question belongs to; the Domain keeps it in the aggregate.</summary>
    internal const string CourseVersionId = "CourseVersionId";

    /// <summary>
    /// The question's place in its version, counted from 1: the order of the aggregate's list, which
    /// <see cref="EditorialDbContext"/> writes before every save.
    /// </summary>
    internal const string Position = "Position";

    private const int EnumLength = 20;

    public void Configure(EntityTypeBuilder<Question> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_version_questions");
        builder.HasKey(question => question.Id);
        // The identifier and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(question => question.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<CourseVersionId>(CourseVersionId).HasColumnName("course_version_id").IsRequired();
        builder.Property<int>(Position).HasColumnName("position").IsRequired();
        builder.Property(question => question.Chapter).HasColumnName("chapter_id").IsRequired();
        builder.Property(question => question.Text).HasColumnName("text").HasMaxLength(QuestionText.MaxLength).IsRequired();
        builder.Property(question => question.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(question => question.Provenance).HasColumnName("provenance").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(question => question.Review).HasColumnName("review").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(question => question.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(question => question.ReviewedAt).HasColumnName("reviewed_at");

        // The database refuses a question whose chapter is gone; the Domain removes a chapter's questions with it.
        // In the database this key and the options' key below are DEFERRABLE INITIALLY DEFERRED, and this one is
        // NO ACTION, since PostgreSQL never defers RESTRICT (migration 20261121223447_DeferQuestionForeignKeys).
        // EF Core sorts a save's commands by table, so a question or option would otherwise be checked before the
        // version's xmin UPDATE could report a lost race, and a save that lost to a removal would fail on the key
        // (23503) instead of answering 409. The model keeps Restrict, which EF Core handles like NoAction on the
        // client. EF Core does not model deferrability: a later migration that drops and recreates either key must
        // make it DEFERRABLE INITIALLY DEFERRED again, and this one NO ACTION.
        builder.HasOne<Chapter>()
            .WithMany()
            .HasForeignKey(question => question.Chapter)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(question => question.Options)
            .WithOne()
            .HasForeignKey(AnswerOptionConfiguration.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(question => question.Options)
            .HasField("options")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(CourseVersionId);
    }
}
