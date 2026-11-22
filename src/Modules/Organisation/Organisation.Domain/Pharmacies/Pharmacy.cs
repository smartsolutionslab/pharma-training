using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// A pharmacy with its memberships. They live inside the aggregate because two rules need all of them at once:
/// there is always at least one active manager, and a person or address is active or invited at most once.
/// </summary>
public sealed class Pharmacy : AggregateRoot<PharmacyId>
{
    /// <summary>How long an invitation link works.</summary>
    public const int InvitationValidityDays = 14;

    private readonly List<Membership> memberships = [];

    private Pharmacy(PharmacyId id, PharmacyName name, Timestamp registeredAt)
        : base(id)
    {
        Name = name;
        RegisteredAt = registeredAt;
    }

    public PharmacyName Name { get; private set; }

    public Timestamp RegisteredAt { get; }

    public IReadOnlyList<Membership> Memberships => memberships;

    /// <summary>The person who registers the pharmacy becomes its first manager at once.</summary>
    public static Pharmacy Register(
        PharmacyId id,
        PharmacyName name,
        MembershipId founderMembership,
        PersonId founder,
        DisplayName founderName,
        EmailAddress founderEmail,
        JobFunction founderJobFunction,
        Timestamp now)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(name).IsNotNull();
        Ensure.That(founderMembership).IsNotNull();
        Ensure.That(founder).IsNotNull();
        Ensure.That(founderName).IsNotNull();
        Ensure.That(founderEmail).IsNotNull();
        Ensure.That(now).IsNotNull();

        var pharmacy = new Pharmacy(id, name, now);
        var membership = Membership.Founder(founderMembership, founderEmail, founder, founderName, founderJobFunction, now);
        pharmacy.memberships.Add(membership);
        pharmacy.Raise(new PharmacyRegistered(id, name));
        pharmacy.Raise(new MembershipStarted(id, membership.Id, founder, founderName, founderJobFunction, MembershipRole.Manager, now));
        return pharmacy;
    }

    public Result<MembershipId, DomainError> Invite(
        MembershipId id,
        EmailAddress email,
        InvitationToken token,
        JobFunction jobFunction,
        MembershipRole role,
        Timestamp now)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(email).IsNotNull();
        Ensure.That(token).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (memberships.Any(m => m.Email == email && m.Status is MembershipStatus.Invited or MembershipStatus.Active))
        {
            return Failure(PharmacyErrors.AlreadyMemberOrInvited(Id, email));
        }

        var expiresAt = now.PlusDays(InvitationValidityDays);
        memberships.Add(Membership.Invited(id, email, InvitationTokenHash.Of(token), jobFunction, role, expiresAt));
        Raise(new MemberInvited(Id, Name, id, email, token, expiresAt));
        return Success(id);
    }

    /// <summary>Sends a fresh link for an invitation that is still open or has run out.</summary>
    public Result<Unit, DomainError> RenewInvitation(MembershipId membership, InvitationToken token, Timestamp now)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(token).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (Find(membership) is not { Status: MembershipStatus.Invited } invited)
        {
            return Failure(PharmacyErrors.NoOpenInvitation(Id, membership));
        }

        var expiresAt = now.PlusDays(InvitationValidityDays);
        invited.RenewInvitation(InvitationTokenHash.Of(token), expiresAt);
        Raise(new MemberInvited(Id, Name, membership, invited.Email, token, expiresAt));
        return Success(Unit.Value);
    }

    /// <param name="token">The secret from the invitation link.</param>
    /// <param name="person">The person accepting, as Keycloak knows them.</param>
    /// <param name="name">The name the person goes by in Keycloak.</param>
    /// <param name="email">The address Keycloak verified for the person; it must be the invited one.</param>
    /// <param name="now">The moment of accepting.</param>
    public Result<Unit, DomainError> AcceptInvitation(
        InvitationToken token,
        PersonId person,
        DisplayName name,
        EmailAddress email,
        Timestamp now)
    {
        Ensure.That(token).IsNotNull();
        Ensure.That(person).IsNotNull();
        Ensure.That(name).IsNotNull();
        Ensure.That(email).IsNotNull();
        Ensure.That(now).IsNotNull();

        var hash = InvitationTokenHash.Of(token);
        if (memberships.SingleOrDefault(m => m.Status == MembershipStatus.Invited && m.TokenHash == hash) is not { } invited)
        {
            return Failure(PharmacyErrors.InvitationNotFound(Id));
        }

        if (invited.InvitationExpiresAt! < now)
        {
            return Failure(PharmacyErrors.InvitationExpired(Id, invited.Id));
        }

        if (invited.Email != email)
        {
            return Failure(PharmacyErrors.InvitationForAnotherAddress(Id, invited.Id));
        }

        if (memberships.Any(m => m.Person == person && m.Status == MembershipStatus.Active))
        {
            return Failure(PharmacyErrors.PersonAlreadyMember(Id, person));
        }

        invited.Activate(person, name, now);
        Raise(new MembershipStarted(Id, invited.Id, person, name, invited.JobFunction, invited.Role, now));
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> RevokeInvitation(MembershipId membership)
    {
        Ensure.That(membership).IsNotNull();

        if (Find(membership) is not { Status: MembershipStatus.Invited } invited)
        {
            return Failure(PharmacyErrors.NoOpenInvitation(Id, membership));
        }

        invited.Revoke();
        Raise(new InvitationRevoked(Id, membership));
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> EndMembership(MembershipId membership, Timestamp now)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (Find(membership) is not { Status: MembershipStatus.Active } active)
        {
            return Failure(PharmacyErrors.NoActiveMembership(Id, membership));
        }

        if (active.IsActiveManager && memberships.Count(m => m.IsActiveManager) == 1)
        {
            return Failure(PharmacyErrors.LastManagerCannotLeave(Id));
        }

        active.End(now);
        Raise(new MembershipEnded(Id, membership, active.Person!, now));
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> ChangeMembership(MembershipId membership, JobFunction jobFunction, MembershipRole role)
    {
        Ensure.That(membership).IsNotNull();

        if (Find(membership) is not { Status: MembershipStatus.Active } active)
        {
            return Failure(PharmacyErrors.NoActiveMembership(Id, membership));
        }

        if (active.IsActiveManager && role != MembershipRole.Manager && memberships.Count(m => m.IsActiveManager) == 1)
        {
            return Failure(PharmacyErrors.LastManagerCannotLeave(Id));
        }

        if (active.JobFunction == jobFunction && active.Role == role)
        {
            return Success(Unit.Value);
        }

        active.Change(jobFunction, role);
        Raise(new MembershipChanged(Id, membership, active.Person!, jobFunction, role));
        return Success(Unit.Value);
    }

    /// <summary>The person's active membership here, if they have one.</summary>
    public Option<Membership> ActiveMembershipOf(PersonId person)
    {
        Ensure.That(person).IsNotNull();

        if (memberships.SingleOrDefault(m => m.Person == person && m.Status == MembershipStatus.Active) is { } found)
        {
            return Option<Membership>.Some(found);
        }

        return Option<Membership>.None;
    }

    private Membership? Find(MembershipId id) => memberships.SingleOrDefault(m => m.Id == id);
}
