using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence.Configurations;

/// <summary>
/// One row per ended membership; its primary key backs the consumer's check that an ending is stored once. Copied from
/// Compliance's <c>MembershipEndingConfiguration.cs</c>.
/// </summary>
public sealed class MembershipEndingCopyConfiguration : IEntityTypeConfiguration<MembershipEndingCopy>
{
    public void Configure(EntityTypeBuilder<MembershipEndingCopy> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign key to the member: an ending may arrive before the membership started.
        builder.ToTable("membership_endings");
        builder.HasKey(ending => ending.Id).HasName("pk_membership_endings");
        builder.Property(ending => ending.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(ending => ending.PersonId).HasColumnName("person_id").IsRequired();
        builder.Property(ending => ending.PharmacyId).HasColumnName("pharmacy_id").IsRequired();

        builder.HasIndex(ending => ending.PersonId).HasDatabaseName("ix_membership_endings_person_id");
    }
}
