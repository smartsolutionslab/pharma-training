using System.Diagnostics.CodeAnalysis;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

/// <summary>
/// Certification's copy of a membership Organisation started: who works in which pharmacy, under which name, in which
/// role, since when. Organisation owns it; Certification stores it once, as it first arrived, and changes it only to
/// forget the holder's name when the person is erased (#115). Whether it has ended is a <see cref="MembershipEnding"/>
/// of its own, a later role a <see cref="MembershipChange"/>.
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

    /// <summary>
    /// The name a certificate's holder is given; <see langword="null"/> once the person was erased (#115). Declared
    /// without <c>?</c> only because the unit tests' in-memory store hands it to <c>Option.Some</c> as it is; every
    /// reader checks it (an erased person's messages are dropped before a name is looked up).
    /// </summary>
    [AllowNull]
    public HolderName HolderName { get; private set; }

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

    /// <summary>Forgets the holder's name (#115): the row stays, since its period still chooses the retained certificates.</summary>
    public void EraseHolderName() => HolderName = null;

    /// <summary>
    /// Whether the membership covers the instant: it started at or before it, and <paramref name="ending"/>, when it has one,
    /// is after it (start inclusive, end exclusive, docs/adr/0032). An ending of another membership is refused.
    /// </summary>
    public bool Covers(Timestamp instant, MembershipEnding? ending)
    {
        Ensure.That(instant).IsNotNull();
        Ensure.That(ending is null || ending.Id == Id ? 0 : 1, nameof(ending)).Satisfies(others => others == 0, "must be the ending of this membership.");

        return StartedAt.Value <= instant.Value && (ending is null || ending.EndedAt.Value > instant.Value);
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
