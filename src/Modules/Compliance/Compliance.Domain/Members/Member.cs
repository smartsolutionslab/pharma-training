using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

/// <summary>
/// Compliance's copy of a membership Organisation started: who works in which pharmacy, under which name, function and
/// role. Organisation owns it; Compliance stores it once, as it first arrived, and never changes it. Whether it has ended
/// is a <see cref="MembershipEnding"/> of its own.
/// </summary>
public sealed class Member : Entity<MembershipId>
{
    private Member(MembershipId id, PersonId person, PharmacyId pharmacy, MemberName name, JobFunction jobFunction, MembershipRole role)
        : base(id)
    {
        Person = person;
        Pharmacy = pharmacy;
        Name = name;
        JobFunction = jobFunction;
        Role = role;
    }

    public PersonId Person { get; }

    public PharmacyId Pharmacy { get; }

    public MemberName Name { get; }

    public JobFunction JobFunction { get; }

    public MembershipRole Role { get; }

    public static Member Started(MembershipId id, PersonId person, PharmacyId pharmacy, MemberName name, JobFunction jobFunction, MembershipRole role)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(name).IsNotNull();
        return new Member(id, person, pharmacy, name, jobFunction, role);
    }

    /// <summary>Forgets the name, for the person's erasure (#115).</summary>
    public void EraseName() => throw new NotImplementedException();
}
