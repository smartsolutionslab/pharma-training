using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence.Configurations;

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    /// <summary>The shadow foreign key to the pharmacy a membership belongs to; the Domain keeps it in the aggregate.</summary>
    internal const string PharmacyId = "PharmacyId";

    /// <summary>The shadow foreign key from an invitation link to the membership that holds it.</summary>
    private const string MembershipId = "MembershipId";

    private const int EnumLength = 20;

    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("memberships");
        builder.HasKey(membership => membership.Id);
        // The identifier and the value objects map to their backing values by convention (module.ddd-depth: strict).
        builder.Property(membership => membership.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<PharmacyId>(PharmacyId).HasColumnName("pharmacy_id").IsRequired();
        builder.Property(membership => membership.Email).HasColumnName("email").HasMaxLength(EmailAddress.MaxLength).IsRequired();
        // Rows from before versions were kept are each at their invitation's first version.
        builder.Property(membership => membership.InvitationVersion).HasColumnName("invitation_version").HasDefaultValue(InvitationVersion.First).IsRequired();
        builder.Property(membership => membership.InvitationMailedFor).HasColumnName("invitation_mailed_for");
        builder.Property(membership => membership.InvitationExpiresAt).HasColumnName("invitation_expires_at");
        builder.Property(membership => membership.Person).HasColumnName("person_id");
        builder.Property(membership => membership.Name).HasColumnName("name").HasMaxLength(DisplayName.MaxLength);
        builder.Property(membership => membership.JobFunction).HasColumnName("job_function").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(membership => membership.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(membership => membership.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(membership => membership.StartedAt).HasColumnName("started_at");
        builder.Property(membership => membership.EndedAt).HasColumnName("ended_at");

        builder.HasIndex(membership => membership.Person);
        builder.HasIndex(PharmacyId);
        // Uniqueness backed twice: Pharmacy.Invite refuses an address that is invited or active, and so does the database
        // for any writer past it. Statuses are stored as text, so the filter compares their names.
        builder.HasIndex(PharmacyId, nameof(Membership.Email)).IsUnique().HasFilter("status IN ('Invited', 'Active')");
        // Backed twice: Pharmacy.AcceptInvitation refuses a person already active in the pharmacy (PersonAlreadyMember),
        // and so does the database. Ended and revoked rows are left out, so someone who left can be accepted again.
        builder.HasIndex(PharmacyId, nameof(Membership.Person)).IsUnique().HasFilter("status = 'Active'");

        // The links of the invitation's current version, one row each, owned by the membership: a renewal, accept or
        // revoke that clears them deletes their rows.
        builder.OwnsMany(membership => membership.InvitationLinks, links =>
        {
            links.ToTable("invitation_links");
            links.WithOwner().HasForeignKey(MembershipId);
            links.Property<MembershipId>(MembershipId).HasColumnName("membership_id");
            links.Property(link => link.Hash).HasColumnName("token_hash").HasMaxLength(InvitationTokenHash.Length).IsRequired();
            links.HasKey(MembershipId, nameof(InvitationLink.Hash));

            // Backed twice: a token is 256 random bits, so no two links share a hash, and the database refuses one that would;
            // an accept finds the one membership a link opens by it.
            links.HasIndex(link => link.Hash).IsUnique();
        });
        builder.Navigation(membership => membership.InvitationLinks)
            .HasField("invitationLinks")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
