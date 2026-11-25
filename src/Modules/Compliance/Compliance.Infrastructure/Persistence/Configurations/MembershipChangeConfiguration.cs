using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence.Configurations;

public sealed class MembershipChangeConfiguration : IEntityTypeConfiguration<MembershipChange>
{
    private const int EnumLength = 16;

    public void Configure(EntityTypeBuilder<MembershipChange> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign key to the member: a change may arrive before the membership started. Every read is by membership,
        // the key's prefix, so no further index.
        builder.ToTable("membership_changes");
        builder.HasKey(change => new { change.Membership, change.Version }).HasName("pk_membership_changes");
        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(change => change.Membership).HasColumnName("membership_id").ValueGeneratedNever();
        builder.Property(change => change.Version).HasColumnName("version").ValueGeneratedNever();
        builder.Property(change => change.Person).HasColumnName("person_id").IsRequired();
        builder.Property(change => change.Pharmacy).HasColumnName("pharmacy_id").IsRequired();
        // As long as Organisation allows a display name to be.
        builder.Property(change => change.Name).HasColumnName("display_name").HasMaxLength(MemberName.MaxLength).IsRequired(false);
        builder.Property(change => change.JobFunction).HasColumnName("job_function").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(change => change.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
    }
}
