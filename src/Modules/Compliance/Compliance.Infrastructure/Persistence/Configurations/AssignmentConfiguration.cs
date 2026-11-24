using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence.Configurations;

public sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    private const int StatusLength = 16;
    private const int TestAccessLength = 32;
    private const string HistoryField = "history";

    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("assignments");
        builder.HasKey(assignment => assignment.Id).HasName("pk_assignments");
        // A save that lost a race to another changes nothing (docs/adr/0002).
        builder.HasRowVersion();

        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(assignment => assignment.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(assignment => assignment.Learner).HasColumnName("learner_id").IsRequired();
        builder.Property(assignment => assignment.Pharmacy).HasColumnName("pharmacy_id").IsRequired();
        builder.Property(assignment => assignment.Course).HasColumnName("course_id").IsRequired();
        builder.Property(assignment => assignment.DueDate).HasColumnName("due_date").IsRequired();
        builder.Property(assignment => assignment.Recurrence).HasColumnName("recurrence_months");
        builder.Property(assignment => assignment.AssignedBy).HasColumnName("assigned_by").IsRequired();
        builder.Property(assignment => assignment.AssignedAt).HasColumnName("assigned_at").IsRequired();
        builder.Property(assignment => assignment.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(StatusLength).IsRequired();
        builder.Property(assignment => assignment.TestAccess).HasColumnName("test_access").HasConversion<string>().HasMaxLength(TestAccessLength).IsRequired();
        builder.Property(assignment => assignment.StartedAt).HasColumnName("started_at");
        builder.Property(assignment => assignment.TestAccessChangedAt).HasColumnName("test_access_changed_at");
        builder.Property(assignment => assignment.CompletedAt).HasColumnName("completed_at");
        builder.Property(assignment => assignment.Certificate).HasColumnName("certificate_id");
        builder.Property(assignment => assignment.Follows).HasColumnName("follows_assignment_id");

        // EF Core refuses a complex collection typed IReadOnlyList, so the history is mapped through its backing field,
        // as one JSON column: a new assignment's is [].
        builder.Ignore(assignment => assignment.History);
        builder.ComplexCollection<List<AssignmentChange>, AssignmentChange>(HistoryField, history => history.ToJson(HistoryField));

        // "At most one active assignment per learner, pharmacy and course", backed twice: the handler refuses the learner
        // it finds active, and this index refuses another writer's row. A save that assigns only inserts rows of distinct
        // keys none of which it saw active, so the index never refuses that save for its own rows (docs/adr/0007).
        // The follow-up of a recurring assignment is created by AssignmentFollowUpRequestedV1Consumer from the host's
        // durable local queue, after the completing save committed (docs/adr/0026). Never put Complete and the follow-up's Add in one save: one row would leave the
        // index and another enter it, and EF Core may insert first.
        builder.HasIndex(assignment => new { assignment.Learner, assignment.Pharmacy, assignment.Course })
            .IsUnique()
            .HasFilter("status IN ('Open', 'InProgress')")
            .HasDatabaseName("ux_assignments_active_learner_pharmacy_course");
        // "One follow-up per assignment", backed twice: the follow-up consumer asks HasFollowUpAsync, and this index
        // refuses a second writer's row, so a redelivery that raced the first throws and its retry finds the follow-up
        // (docs/adr/0026). PostgreSQL counts NULLs as distinct, so assignments made by hand never collide here.
        builder.HasIndex(assignment => assignment.Follows).IsUnique().HasDatabaseName("ux_assignments_follows_assignment_id");
        builder.HasIndex(assignment => assignment.Pharmacy).HasDatabaseName("ix_assignments_pharmacy_id");
        builder.HasIndex(assignment => assignment.Learner).HasDatabaseName("ix_assignments_learner_id");
    }
}
