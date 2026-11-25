namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

/// <summary>
/// That Organisation changed a membership's job function or role, at a version after its start. Kept apart from the
/// <see cref="Member"/>, and stored as it arrived even before the membership started or after a newer change: which state
/// is current is decided when it is read (<see cref="CurrentMembership"/>). Its identity is the membership and the version.
/// </summary>
public sealed class MembershipChange
{
    private MembershipChange(MembershipId membership, PersonId person, PharmacyId pharmacy, MembershipVersion version, MemberName name, JobFunction jobFunction, MembershipRole role)
    {
        Membership = membership;
        Person = person;
        Pharmacy = pharmacy;
        Version = version;
        Name = name;
        JobFunction = jobFunction;
        Role = role;
    }

    public MembershipId Membership { get; }

    public PersonId Person { get; }

    public PharmacyId Pharmacy { get; }

    public MembershipVersion Version { get; }

    /// <summary>The name as it arrived; null once the person is erased (#115).</summary>
    public MemberName? Name { get; private set; }

    public JobFunction JobFunction { get; }

    public MembershipRole Role { get; }

    /// <summary>A change at a version after the start; the start itself is version 1 and arrives as a <see cref="Member"/>.</summary>
    public static MembershipChange Of(MembershipId membership, PersonId person, PharmacyId pharmacy, MembershipVersion version, MemberName name, JobFunction jobFunction, MembershipRole role)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(version).IsNotNull();
        Ensure.That(version.Value, nameof(version)).Satisfies(_ => version > MembershipVersion.First, "must come after the membership's start, which is version 1.");
        Ensure.That(name).IsNotNull();
        return new MembershipChange(membership, person, pharmacy, version, name, jobFunction, role);
    }

    /// <summary>Forgets the name, for the person's erasure (#115).</summary>
    public void EraseName() => Name = null;
}
