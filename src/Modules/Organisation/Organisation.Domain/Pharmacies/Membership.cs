using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>A person's place in one pharmacy, from the invitation to the day they leave.</summary>
public sealed class Membership : Entity<MembershipId>
{
    private Membership(MembershipId id)
        : base(id)
    {
    }

    public EmailAddress Email { get; private set; } = default!;

    public InvitationToken? Token { get; private set; }

    public Timestamp? InvitationExpiresAt { get; private set; }

    public PersonId? Person { get; private set; }

    public DisplayName? Name { get; private set; }

    public JobFunction JobFunction { get; private set; }

    public MembershipRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public Timestamp? StartedAt { get; private set; }

    public Timestamp? EndedAt { get; private set; }

    internal bool IsActiveManager => Status == MembershipStatus.Active && Role == MembershipRole.Manager;

    internal static Membership Invited(MembershipId id, EmailAddress email, InvitationToken token, JobFunction jobFunction, MembershipRole role, Timestamp expiresAt) =>
        new(id) { Email = email, Token = token, InvitationExpiresAt = expiresAt, JobFunction = jobFunction, Role = role, Status = MembershipStatus.Invited };

    internal static Membership Founder(MembershipId id, EmailAddress email, PersonId person, DisplayName name, JobFunction jobFunction, Timestamp now) =>
        new(id) { Email = email, Person = person, Name = name, JobFunction = jobFunction, Role = MembershipRole.Manager, Status = MembershipStatus.Active, StartedAt = now };

    internal void Activate(PersonId person, DisplayName name, Timestamp now)
    {
        Person = person;
        Name = name;
        Status = MembershipStatus.Active;
        StartedAt = now;
        Token = null; // a used link never works again
        InvitationExpiresAt = null;
    }

    internal void Revoke()
    {
        Status = MembershipStatus.Revoked;
        Token = null;
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
    }

    internal void RenewInvitation(InvitationToken token, Timestamp expiresAt)
    {
        Token = token;
        InvitationExpiresAt = expiresAt;
    }
}
