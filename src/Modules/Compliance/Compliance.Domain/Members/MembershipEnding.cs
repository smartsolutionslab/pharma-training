using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

/// <summary>
/// That Organisation ended a membership. Kept apart from the <see cref="Member"/>, so it is stored even when it arrives
/// before the membership started, and the member is never deleted.
/// </summary>
public sealed class MembershipEnding : Entity<MembershipId>
{
    private MembershipEnding(MembershipId id, PersonId person, PharmacyId pharmacy)
        : base(id)
    {
        Person = person;
        Pharmacy = pharmacy;
    }

    public PersonId Person { get; }

    public PharmacyId Pharmacy { get; }

    public static MembershipEnding Of(MembershipId id, PersonId person, PharmacyId pharmacy)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        return new MembershipEnding(id, person, pharmacy);
    }
}
