using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence.Configurations;

public sealed class MembershipEndingConfiguration : IEntityTypeConfiguration<MembershipEnding>
{
    public void Configure(EntityTypeBuilder<MembershipEnding> builder)
    {
        Ensure.That(builder).IsNotNull();

        // No foreign key to the membership: an ending may arrive before the membership started.
        builder.ToTable("membership_endings");
        builder.HasKey(ending => ending.Id).HasName("pk_membership_endings");
        builder.Property(ending => ending.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(ending => ending.Person).HasColumnName("person_id").IsRequired();
        builder.Property(ending => ending.Pharmacy).HasColumnName("pharmacy_id").IsRequired();
        builder.Property(ending => ending.EndedAt).HasColumnName("ended_at").IsRequired();
    }
}
