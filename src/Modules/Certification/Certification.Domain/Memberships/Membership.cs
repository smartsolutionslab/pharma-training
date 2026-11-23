using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>
/// Certification's copy of a membership Organisation started: who works in which pharmacy, under which name, since
/// when. Organisation owns it; Certification stores it once, as it first arrived, and never changes it. Whether it has
/// ended is a <see cref="MembershipEnding"/> of its own.
/// </summary>
public sealed class Membership : Entity<MembershipId>
{
    private Membership(MembershipId id, PersonId person, PharmacyId pharmacy, HolderName holderName, Timestamp startedAt)
        : base(id)
    {
        Person = person;
        Pharmacy = pharmacy;
        HolderName = holderName;
        StartedAt = startedAt;
    }

    public PersonId Person { get; }

    public PharmacyId Pharmacy { get; }

    public HolderName HolderName { get; }

    public Timestamp StartedAt { get; }

    public static Membership Started(MembershipId id, PersonId person, PharmacyId pharmacy, HolderName holderName, Timestamp startedAt)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(holderName).IsNotNull();
        Ensure.That(startedAt).IsNotNull();
        return new Membership(id, person, pharmacy, holderName, startedAt);
    }
}
