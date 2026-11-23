using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

/// <summary>
/// One row per membership and version; the key backs the consumer's check that a change is stored once. Copied from
/// Compliance's <c>MembershipChangeConfiguration.cs</c>, without name, job function and role.
/// </summary>
public sealed class MembershipChangeCopyConfiguration : IEntityTypeConfiguration<MembershipChangeCopy>
{
    public void Configure(EntityTypeBuilder<MembershipChangeCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign key to the member: a change may arrive before the membership started.
        builder.ToTable("membership_changes");
        builder.HasKey(change => new { change.MembershipId, change.Version }).HasName("pk_membership_changes");
        builder.Property(change => change.MembershipId).HasColumnName("membership_id").ValueGeneratedNever();
        builder.Property(change => change.Version).HasColumnName("version").ValueGeneratedNever();
        builder.Property(change => change.PersonId).HasColumnName("person_id").IsRequired();
        builder.Property(change => change.PharmacyId).HasColumnName("pharmacy_id").IsRequired();

        builder.HasIndex(change => change.PersonId).HasDatabaseName("ix_membership_changes_person_id");
    }
}
