using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence.Configurations;

/// <summary>An answer option of a copied test question, with its correctness: what scoring reads and a learner never sees.</summary>
public sealed class TestOptionCopyConfiguration : IEntityTypeConfiguration<TestOptionCopy>
{
    public void Configure(EntityTypeBuilder<TestOptionCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("test_definition_options");
        builder.HasKey(option => new { option.CourseVersionId, option.QuestionId, option.OptionId }).HasName("pk_test_definition_options");
        builder.Property(option => option.CourseVersionId).HasColumnName("course_version_id").ValueGeneratedNever();
        builder.Property(option => option.QuestionId).HasColumnName("question_id").ValueGeneratedNever();
        builder.Property(option => option.OptionId).HasColumnName("option_id").ValueGeneratedNever();
        builder.Property(option => option.Position).HasColumnName("position").IsRequired();
        builder.Property(option => option.Text).HasColumnName("text").IsRequired();
        builder.Property(option => option.IsCorrect).HasColumnName("is_correct").IsRequired();
    }
}
