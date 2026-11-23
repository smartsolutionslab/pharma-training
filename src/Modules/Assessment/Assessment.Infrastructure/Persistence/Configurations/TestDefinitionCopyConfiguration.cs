using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence.Configurations;

/// <summary>One row per course version whose test Assessment received; its primary key backs the consumer's check that a version is stored once.</summary>
public sealed class TestDefinitionCopyConfiguration : IEntityTypeConfiguration<TestDefinitionCopy>
{
    public void Configure(EntityTypeBuilder<TestDefinitionCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("test_definitions");
        builder.HasKey(definition => definition.CourseVersionId).HasName("pk_test_definitions");
        builder.Property(definition => definition.CourseVersionId).HasColumnName("course_version_id").ValueGeneratedNever();
        builder.Property(definition => definition.CourseId).HasColumnName("course_id").IsRequired();
        builder.Property(definition => definition.PassingScorePercent).HasColumnName("passing_score_percent").IsRequired();
        builder.Property(definition => definition.MaxAttempts).HasColumnName("max_attempts").IsRequired();

        builder.HasMany(definition => definition.Questions)
            .WithOne()
            .HasForeignKey(question => question.CourseVersionId)
            .HasConstraintName("fk_test_definition_questions_test_definitions_course_version_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
