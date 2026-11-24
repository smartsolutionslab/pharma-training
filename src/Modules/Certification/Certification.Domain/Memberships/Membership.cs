using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>
/// Certification's copy of a membership Organisation started: who works in which pharmacy, under which name, in which
/// role, since when. Organisation owns it; Certification stores it once, as it first arrived, and never changes it.
/// Whether it has ended is a <see cref="MembershipEnding"/> of its own, a later role a <see cref="MembershipChange"/>.
/// </summary>
public sealed class Membership : Entity<MembershipId>
{
    private Membership(MembershipId id, PersonId person, PharmacyId pharmacy, HolderName holderName, MembershipRole role, Timestamp startedAt)
        : base(id)
    {
        Person = person;
        Pharmacy = pharmacy;
        HolderName = holderName;
        Role = role;
        StartedAt = startedAt;
    }

    public PersonId Person { get; }

    public PharmacyId Pharmacy { get; }

    public HolderName HolderName { get; }

    /// <summary>The role the membership started in; <see cref="CurrentRole"/> is the one it has now.</summary>
    public MembershipRole Role { get; }

    public Timestamp StartedAt { get; }

    public static Membership Started(MembershipId id, PersonId person, PharmacyId pharmacy, HolderName holderName, MembershipRole role, Timestamp startedAt)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(holderName).IsNotNull();
        Ensure.That(startedAt).IsNotNull();
        return new Membership(id, person, pharmacy, holderName, role, startedAt);
    }

    /// <summary>
    /// The role the membership has now: that of its change with the highest version, whatever order the changes came in,
    /// else the one it started in (docs/adr/0015). A change of another membership is refused.
    /// </summary>
    public MembershipRole CurrentRole(IReadOnlyCollection<MembershipChange> changes)
    {
        Ensure.That(changes).IsNotNull();
        Ensure.That(changes.Count(change => change.Membership != Id), nameof(changes)).Satisfies(others => others == 0, "must all be changes of this membership.");

        return changes.MaxBy(change => change.Version) is { } newest ? newest.Role : Role;
    }
}
