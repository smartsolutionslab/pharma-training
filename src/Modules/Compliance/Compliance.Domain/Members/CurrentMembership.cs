namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

/// <summary>
/// A membership as it stands now, from the member as it started and the changes that have arrived, in whatever order:
/// the change with the highest version wins, so an older one arriving late changes nothing; without changes the member
/// is as it started, at version 1. Person and pharmacy never change and come from the member (docs/adr/0015).
/// </summary>
public sealed class CurrentMembership
{
    private CurrentMembership(MembershipId membership, PersonId person, PharmacyId pharmacy, MemberName? name, JobFunction jobFunction, MembershipRole role, MembershipVersion version)
    {
        Membership = membership;
        Person = person;
        Pharmacy = pharmacy;
        Name = name;
        JobFunction = jobFunction;
        Role = role;
        Version = version;
    }

    public MembershipId Membership { get; }

    public PersonId Person { get; }

    public PharmacyId Pharmacy { get; }

    /// <summary>The current name; null once the person is erased (#115).</summary>
    public MemberName? Name { get; }

    public JobFunction JobFunction { get; }

    public MembershipRole Role { get; }

    public MembershipVersion Version { get; }

    /// <summary>The member with its changes, each of which must belong to that membership.</summary>
    public static CurrentMembership Of(Member member, IReadOnlyCollection<MembershipChange> changes)
    {
        Ensure.That(member).IsNotNull();
        Ensure.That(changes).IsNotNull();
        foreach (var change in changes)
        {
            Ensure.That(change, nameof(changes)).IsNotNull();
            Ensure.That(change.Membership.ToString(), nameof(changes)).Satisfies(_ => change.Membership == member.Id, $"change {change.Version} belongs to another membership than {member.Id}.");
        }

        if (changes.Count == 0)
        {
            return new CurrentMembership(member.Id, member.Person, member.Pharmacy, member.Name, member.JobFunction, member.Role, MembershipVersion.First);
        }

        var latest = changes.OrderByDescending(change => change.Version).First();
        return new CurrentMembership(member.Id, member.Person, member.Pharmacy, latest.Name, latest.JobFunction, latest.Role, latest.Version);
    }

    /// <summary>Whether the membership's current role grants the capability.</summary>
    public bool Grants(Capability capability) => Role.Grants(capability);
}
