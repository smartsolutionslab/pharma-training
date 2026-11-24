using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>
/// That Organisation changed a membership's role, at a version after its start (docs/adr/0015). Kept apart from the
/// <see cref="Membership"/>, and stored as it arrived even before the membership started or after a newer change: which
/// role is current is decided when it is read (<see cref="Membership.CurrentRole"/>). Its identity is the membership and
/// the version. Only the role is kept, no name and no job function: Certification needs nothing more of a change.
/// </summary>
public sealed class MembershipChange
{
    private MembershipChange(MembershipId membership, PersonId person, PharmacyId pharmacy, MembershipVersion version, MembershipRole role)
    {
        Membership = membership;
        Person = person;
        Pharmacy = pharmacy;
        Version = version;
        Role = role;
    }

    public MembershipId Membership { get; }

    public PersonId Person { get; }

    public PharmacyId Pharmacy { get; }

    public MembershipVersion Version { get; }

    public MembershipRole Role { get; }

    /// <summary>A change at a version after the start; the start itself is version 1 and arrives as a <see cref="Membership"/>.</summary>
    public static MembershipChange Of(MembershipId membership, PersonId person, PharmacyId pharmacy, MembershipVersion version, MembershipRole role)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(version).IsNotNull();
        Ensure.That(version.Value, nameof(version)).Satisfies(_ => version > MembershipVersion.First, "must come after the membership's start, which is version 1.");
        return new MembershipChange(membership, person, pharmacy, version, role);
    }
}
