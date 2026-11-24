using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>A person's place in one pharmacy, from the invitation to the day they leave.</summary>
public sealed class Membership : Entity<MembershipId>
{
    private readonly List<InvitationLink> invitationLinks = [];

    private Membership(MembershipId id)
        : base(id)
    {
    }

    public EmailAddress Email { get; private set; } = default!;

    /// <summary>
    /// The links issued for the open invitation's current version, by their tokens' hashes; the tokens themselves are
    /// never kept. Each of them opens the invitation, since the invitee may hold any of their mails.
    /// </summary>
    public IReadOnlyList<InvitationLink> InvitationLinks => invitationLinks;

    /// <summary>The invitation's version: 1 at the invite, the next at each renewal. A founder, never invited, keeps 1, never mailed.</summary>
    public InvitationVersion InvitationVersion { get; private set; } = InvitationVersion.First;

    /// <summary>Which state of the membership this is: 1 from its start, the next at each change of job function or role.</summary>
    public MembershipVersion Version { get; private set; } = MembershipVersion.First;

    /// <summary>The invitation version whose mail was sent, if any; a version that is mailed is not mailed again.</summary>
    public InvitationVersion? InvitationMailedFor { get; private set; }

    public Timestamp? InvitationExpiresAt { get; private set; }

    /// <summary>When the newest invitation mail was requested (invite or renewal); none for a founder, never mailed.</summary>
    public Timestamp? InvitationRequestedAt { get; private set; }

    public PersonId? Person { get; private set; }

    public DisplayName? Name { get; private set; }

    public JobFunction JobFunction { get; private set; }

    public MembershipRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public Timestamp? StartedAt { get; private set; }

    public Timestamp? EndedAt { get; private set; }

    internal bool IsActiveManager => Status == MembershipStatus.Active && Role == MembershipRole.Manager;

    internal static Membership Invited(MembershipId id, EmailAddress email, JobFunction jobFunction, MembershipRole role, Timestamp now, Timestamp expiresAt) =>
        new(id) { Email = email, InvitationRequestedAt = now, InvitationExpiresAt = expiresAt, JobFunction = jobFunction, Role = role, Status = MembershipStatus.Invited };

    internal static Membership Founder(MembershipId id, EmailAddress email, PersonId person, DisplayName name, JobFunction jobFunction, Timestamp now) =>
        new(id) { Email = email, Person = person, Name = name, JobFunction = jobFunction, Role = MembershipRole.Manager, Status = MembershipStatus.Active, StartedAt = now };

    internal void Activate(PersonId person, DisplayName name, Timestamp now)
    {
        Person = person;
        Name = name;
        Status = MembershipStatus.Active;
        StartedAt = now;
        invitationLinks.Clear(); // a used invitation's links never work again
        InvitationExpiresAt = null;
    }

    internal void Revoke()
    {
        Status = MembershipStatus.Revoked;
        invitationLinks.Clear();
    }

    internal void End(Timestamp now)
    {
        Status = MembershipStatus.Ended;
        EndedAt = now;
    }

    internal void Change(JobFunction jobFunction, MembershipRole role)
    {
        JobFunction = jobFunction;
        Role = role;
        Version = Version.Next();
    }

    /// <summary>Whether one of the current version's links has this hash.</summary>
    internal bool Holds(InvitationTokenHash tokenHash) => invitationLinks.Any(link => link.Hash == tokenHash);

    /// <summary>Whether the current version holds as many links as it may (<see cref="Pharmacy.MaxInvitationLinksPerVersion"/>).</summary>
    internal bool HoldsMostLinks() => invitationLinks.Count >= Pharmacy.MaxInvitationLinksPerVersion;

    /// <summary>A link mailed now, beside the version's earlier ones, which keep working.</summary>
    internal void IssueLink(InvitationTokenHash tokenHash) => invitationLinks.Add(InvitationLink.Of(tokenHash));

    /// <summary>Takes back the link with this hash, whose mail never left; answers whether the version held it.</summary>
    internal bool WithdrawLink(InvitationTokenHash tokenHash) => invitationLinks.RemoveAll(link => link.Hash == tokenHash) > 0;

    internal void RecordMailed(InvitationVersion version) => InvitationMailedFor = version;

    /// <summary>The instant from which the invitation may be renewed again; none while no mail was ever requested.</summary>
    internal Option<Timestamp> RenewableFrom(RenewCooldown cooldown)
    {
        if (InvitationRequestedAt is { } requestedAt)
        {
            return Option<Timestamp>.Some(requestedAt.Plus(cooldown.Value));
        }

        return Option<Timestamp>.None;
    }

    internal void RenewInvitation(Timestamp now, Timestamp expiresAt)
    {
        invitationLinks.Clear(); // the old links stop working at once; the renewal's mail issues the new one
        InvitationRequestedAt = now;
        InvitationExpiresAt = expiresAt;
        InvitationVersion = InvitationVersion.Next();
    }
}
