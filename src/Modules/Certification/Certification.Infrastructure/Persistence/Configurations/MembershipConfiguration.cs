using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence.Configurations;

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    private const int EnumLength = 16;

    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("memberships");
        builder.HasKey(membership => membership.Id).HasName("pk_memberships");
        // The identifiers and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(membership => membership.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(membership => membership.Person).HasColumnName("person_id").IsRequired();
        builder.Property(membership => membership.Pharmacy).HasColumnName("pharmacy_id").IsRequired();
        // As long as Organisation allows a display name to be; NULL once the person was erased (#115).
        builder.Property(membership => membership.HolderName).HasColumnName("holder_name").HasMaxLength(HolderName.MaxLength).IsRequired(false);
        builder.Property(membership => membership.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(membership => membership.StartedAt).HasColumnName("started_at").IsRequired();

        // Backs finding the name of a person's latest membership.
        builder.HasIndex(membership => membership.Person).HasDatabaseName("ix_memberships_person_id");
        // Backs choosing an export's certificates by their holders' memberships in the pharmacy.
        builder.HasIndex(membership => membership.Pharmacy).HasDatabaseName("ix_memberships_pharmacy_id");
    }
}
