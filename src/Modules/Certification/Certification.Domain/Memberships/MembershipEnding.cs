using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>
/// That Organisation ended a membership. Kept apart from the <see cref="Membership"/>, so it is stored even when it
/// arrives before the membership started, and the membership is never deleted.
/// </summary>
public sealed class MembershipEnding : Entity<MembershipId>
{
    private MembershipEnding(MembershipId id, PersonId person, PharmacyId pharmacy, Timestamp endedAt)
        : base(id)
    {
        Person = person;
        Pharmacy = pharmacy;
        EndedAt = endedAt;
    }

    public PersonId Person { get; }

    public PharmacyId Pharmacy { get; }

    public Timestamp EndedAt { get; }

    public static MembershipEnding Of(MembershipId id, PersonId person, PharmacyId pharmacy, Timestamp endedAt)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(endedAt).IsNotNull();
        return new MembershipEnding(id, person, pharmacy, endedAt);
    }
}
