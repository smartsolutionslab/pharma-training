using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence.Configurations;

public sealed class MembershipChangeConfiguration : IEntityTypeConfiguration<MembershipChange>
{
    private const int EnumLength = 16;

    public void Configure(EntityTypeBuilder<MembershipChange> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign key to the membership: a change may arrive before the membership started. Every read is by
        // membership, the key's prefix, so no further index.
        builder.ToTable("membership_changes");
        builder.HasKey(change => new { change.Membership, change.Version }).HasName("pk_membership_changes");
        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(change => change.Membership).HasColumnName("membership_id").ValueGeneratedNever();
        builder.Property(change => change.Version).HasColumnName("version").ValueGeneratedNever();
        builder.Property(change => change.Person).HasColumnName("person_id").IsRequired();
        builder.Property(change => change.Pharmacy).HasColumnName("pharmacy_id").IsRequired();
        builder.Property(change => change.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
    }
}
