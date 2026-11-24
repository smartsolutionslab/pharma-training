using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence.Configurations;

public sealed class LearningSessionStartConfiguration : IEntityTypeConfiguration<LearningSessionStart>
{
    public void Configure(EntityTypeBuilder<LearningSessionStart> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign keys: the copies arrive in any order, before or after the course versions and assignments.
        builder.ToTable("learning_session_starts");
        builder.HasKey(start => start.Id).HasName("pk_learning_session_starts");
        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(start => start.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(start => start.Learner).HasColumnName("learner_id").IsRequired();
        builder.Property(start => start.Course).HasColumnName("course_id").IsRequired();
        builder.Property(start => start.Version).HasColumnName("course_version_id").IsRequired();
        builder.Property(start => start.StartedAt).HasColumnName("started_at").IsRequired();

        // Backs reading a learner's starts of the courses on the dashboard.
        builder.HasIndex(start => new { start.Learner, start.Course }).HasDatabaseName("ix_learning_session_starts_learner_id_course_id");
    }
}
