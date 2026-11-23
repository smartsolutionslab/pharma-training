using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence.Configurations;

/// <summary>A question of a copied test definition, keyed with its version so two versions stay apart.</summary>
public sealed class TestQuestionCopyConfiguration : IEntityTypeConfiguration<TestQuestionCopy>
{
    public void Configure(EntityTypeBuilder<TestQuestionCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("test_definition_questions");
        builder.HasKey(question => new { question.CourseVersionId, question.QuestionId }).HasName("pk_test_definition_questions");
        builder.Property(question => question.CourseVersionId).HasColumnName("course_version_id").ValueGeneratedNever();
        builder.Property(question => question.QuestionId).HasColumnName("question_id").ValueGeneratedNever();
        builder.Property(question => question.Position).HasColumnName("position").IsRequired();
        builder.Property(question => question.ChapterId).HasColumnName("chapter_id").IsRequired();
        builder.Property(question => question.Type).HasColumnName("type").HasConversion<string>().IsRequired();
        builder.Property(question => question.Text).HasColumnName("text").IsRequired();

        builder.HasMany(question => question.Options)
            .WithOne()
            .HasForeignKey(option => new { option.CourseVersionId, option.QuestionId })
            .HasConstraintName("fk_test_definition_options_test_definition_questions")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
