using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence.Configurations;

public sealed class PharmacyConfiguration : IEntityTypeConfiguration<Pharmacy>
{
    public void Configure(EntityTypeBuilder<Pharmacy> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("pharmacies");
        builder.HasKey(pharmacy => pharmacy.Id);
        // The identifier and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(pharmacy => pharmacy.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(pharmacy => pharmacy.Name).HasColumnName("name").HasMaxLength(PharmacyName.MaxLength).IsRequired();
        builder.Property(pharmacy => pharmacy.RegisteredAt).HasColumnName("registered_at").IsRequired();
        // The tally of invitation mails toward the daily cap (docs/adr/0004): the UTC day of the latest counted mail and
        // how many that day. The row version below serialises two requests that count at once.
        builder.Property(pharmacy => pharmacy.InvitationMailDay).HasColumnName("invitation_mail_day");
        builder.Property(pharmacy => pharmacy.InvitationMailsThatDay).HasColumnName("invitation_mails_that_day").HasDefaultValue(InvitationMailCount.Zero).IsRequired();
        // The pharmacy is the consistency boundary: a save of it or of any of its memberships checks and moves this on.
        builder.HasRowVersion();

        builder.HasMany(pharmacy => pharmacy.Memberships)
            .WithOne()
            .HasForeignKey(MembershipConfiguration.PharmacyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(pharmacy => pharmacy.Memberships)
            .HasField("memberships")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
