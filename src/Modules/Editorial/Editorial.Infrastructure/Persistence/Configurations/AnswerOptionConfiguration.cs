using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence.Configurations;

public sealed class AnswerOptionConfiguration : IEntityTypeConfiguration<AnswerOption>
{
    /// <summary>The shadow foreign key to the question an option belongs to; the Domain keeps it in the question.</summary>
    internal const string QuestionId = "QuestionId";

    /// <summary>
    /// The option's place in its question, counted from 1: the order of the question's list, which
    /// <see cref="EditorialDbContext"/> writes before every save.
    /// </summary>
    internal const string Position = "Position";

    private const int EnumLength = 20;

    public void Configure(EntityTypeBuilder<AnswerOption> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("course_version_answer_options");
        builder.HasKey(option => option.Id);
        // The identifier and the value object map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(option => option.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<QuestionId>(QuestionId).HasColumnName("question_id").IsRequired();
        builder.Property<int>(Position).HasColumnName("position").IsRequired();
        builder.Property(option => option.Text).HasColumnName("text").HasMaxLength(AnswerText.MaxLength).IsRequired();
        builder.Property(option => option.Correctness).HasColumnName("correctness").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();

        builder.HasIndex(QuestionId);
    }
}
