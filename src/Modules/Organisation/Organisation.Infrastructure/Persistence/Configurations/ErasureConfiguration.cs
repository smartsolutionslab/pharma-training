using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence.Configurations;

/// <summary>
/// The erasure log (#115): one row per request and one per module that completed its part. It holds neither the erased
/// person's id, name nor address, only the support staff member who asked.
/// </summary>
public sealed class ErasureConfiguration : IEntityTypeConfiguration<Erasure>
{
    /// <summary>The shadow foreign key from a step to the erasure it belongs to.</summary>
    private const string ErasureId = "ErasureId";

    private const int EnumLength = 20;

    public void Configure(EntityTypeBuilder<Erasure> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("erasures");
        builder.HasKey(erasure => erasure.Id);
        // The identifier and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(erasure => erasure.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(erasure => erasure.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(erasure => erasure.RequestedBy).HasColumnName("requested_by").IsRequired();
        builder.Property(erasure => erasure.Confirmation).HasColumnName("confirmation").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(erasure => erasure.ConfirmationHandledAt).HasColumnName("confirmation_handled_at");
        // A redelivered confirmation and, later, the modules' completions race on this; the loser saves nothing and retries.
        builder.HasRowVersion();

        // The completed steps, one row each, owned by the erasure; a module completes its step once.
        builder.OwnsMany(erasure => erasure.Steps, steps =>
        {
            steps.ToTable("erasure_steps");
            steps.WithOwner().HasForeignKey(ErasureId);
            steps.Property<ErasureId>(ErasureId).HasColumnName("erasure_id");
            steps.Property(step => step.Module).HasColumnName("module").HasMaxLength(ErasureModule.MaxLength).IsRequired();
            steps.Property(step => step.CompletedAt).HasColumnName("completed_at").IsRequired();
            steps.HasKey(ErasureId, nameof(ErasureStep.Module));
        });
        builder.Navigation(erasure => erasure.Steps)
            .HasField("steps")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
