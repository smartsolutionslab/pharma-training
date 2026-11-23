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

    /// <summary>
    /// How many links one invitation version may hold, so a mail that keeps being retried cannot mint links without
    /// limit. The invitee holds the earlier ones; a renewal clears them.
    /// </summary>
    public const int MaxInvitationLinksPerVersion = 5;

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

    /// <summary>The UTC day of the pharmacy's latest counted invitation mail; none before its first.</summary>
    public UtcDay? InvitationMailDay { get; private set; }

    /// <summary>How many invitation mails the pharmacy requested on <see cref="InvitationMailDay"/>.</summary>
    public InvitationMailCount InvitationMailsThatDay { get; private set; } = InvitationMailCount.Zero;

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

    /// <summary>
    /// Invites the address, valid for 14 days from now. The invitation holds no link yet: its mail issues one
    /// (<see cref="IssueInvitationLink"/>), so no token exists before the mail is sent. Its mail counts toward the
    /// pharmacy's daily cap, which refuses it once reached.
    /// </summary>
    public Result<MembershipId, DomainError> Invite(MembershipId id, EmailAddress email, JobFunction jobFunction, MembershipRole role, Timestamp now, InvitationMailLimits limits)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(email).IsNotNull();
        Ensure.That(now).IsNotNull();
        Ensure.That(limits).IsNotNull();

        if (memberships.Any(m => m.Email == email && m.Status is MembershipStatus.Invited or MembershipStatus.Active))
        {
            return Failure(PharmacyErrors.AlreadyMemberOrInvited(Id, email));
        }

        if (DailyCapReached(now, limits.DailyMailCap) is Some<Timestamp> tomorrow)
        {
            return Failure(PharmacyErrors.TooManyInvitationsToday(Id, tomorrow.Value));
        }

        var expiresAt = now.PlusDays(InvitationValidityDays);
        CountInvitationMail(now);
        memberships.Add(Membership.Invited(id, email, jobFunction, role, now, expiresAt));
        Raise(new MemberInvited(Id, id, expiresAt));
        return Success(id);
    }

    /// <summary>
    /// Renews an invitation that is still open or has run out: its next version, valid for 14 days from now, and every
    /// earlier link stops working at once. Its new mail issues the new link. It is refused within the cooldown since
    /// the invitation's latest mail request, and once the pharmacy's daily cap is reached.
    /// </summary>
    public Result<Unit, DomainError> RenewInvitation(MembershipId membership, Timestamp now, InvitationMailLimits limits)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(now).IsNotNull();
        Ensure.That(limits).IsNotNull();

        if (Find(membership) is not { Status: MembershipStatus.Invited } invited)
        {
            return Failure(PharmacyErrors.NoOpenInvitation(Id, membership));
        }

        if (invited.RenewableFrom(limits.RenewCooldown) is Some<Timestamp> renewableFrom && now < renewableFrom.Value)
        {
            return Failure(PharmacyErrors.InvitationRenewedTooSoon(Id, membership, renewableFrom.Value));
        }

        if (DailyCapReached(now, limits.DailyMailCap) is Some<Timestamp> tomorrow)
        {
            return Failure(PharmacyErrors.TooManyInvitationsToday(Id, tomorrow.Value));
        }

        var expiresAt = now.PlusDays(InvitationValidityDays);
        CountInvitationMail(now);
        invited.RenewInvitation(now, expiresAt);
        Raise(new MemberInvited(Id, membership, expiresAt));
        return Success(Unit.Value);
    }

    /// <summary>
    /// Issues a link of an open invitation that has not run out: the token's hash joins those issued for the current
    /// version, which keep working, up to <see cref="MaxInvitationLinksPerVersion"/>. Answers its expiry.
    /// </summary>
    public Result<Timestamp, DomainError> IssueInvitationLink(MembershipId membership, InvitationToken token, Timestamp now)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(token).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (Find(membership) is not { Status: MembershipStatus.Invited, InvitationExpiresAt: { } expiresAt } invited)
        {
            return Failure(PharmacyErrors.NoOpenInvitation(Id, membership));
        }

        // A link that would be dead on arrival is never issued; the manager renews a lapsed invitation.
        if (expiresAt < now)
        {
            return Failure(PharmacyErrors.InvitationExpired(Id, membership));
        }

        if (invited.HoldsMostLinks())
        {
            return Failure(PharmacyErrors.InvitationLinkLimitReached(Id, membership));
        }

        invited.IssueLink(InvitationTokenHash.Of(token));
        Raise(new InvitationLinkIssued(Id, membership));
        return Success(expiresAt);
    }

    /// <summary>
    /// Takes back a link whose mail never reached the mail server, so it neither opens the invitation nor counts towards
    /// <see cref="MaxInvitationLinksPerVersion"/>. A link the invitation no longer holds (it was renewed, accepted or
    /// revoked since) is already gone: nothing changes then.
    /// </summary>
    public Result<Unit, DomainError> WithdrawInvitationLink(MembershipId membership, InvitationToken token)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(token).IsNotNull();

        if (Find(membership) is not { Status: MembershipStatus.Invited } invited)
        {
            return Failure(PharmacyErrors.NoOpenInvitation(Id, membership));
        }

        if (invited.WithdrawLink(InvitationTokenHash.Of(token)))
        {
            Raise(new InvitationLinkWithdrawn(Id, membership));
        }

        return Success(Unit.Value);
    }

    /// <summary>
    /// Records that the mail of the open invitation's version was sent, so a redelivery of its request does not mail it
    /// again. A version that is no longer the open one (renewed, accepted or revoked since) is refused.
    /// </summary>
    public Result<Unit, DomainError> RecordInvitationMailed(MembershipId membership, InvitationVersion version)
    {
        Ensure.That(membership).IsNotNull();
        Ensure.That(version).IsNotNull();

        if (Find(membership) is not { Status: MembershipStatus.Invited } invited || invited.InvitationVersion != version)
        {
            return Failure(PharmacyErrors.NoOpenInvitation(Id, membership));
        }

        invited.RecordMailed(version);
        Raise(new InvitationMailed(Id, membership, version));
        return Success(Unit.Value);
    }

    /// <param name="token">The secret from the invitation link.</param>
    /// <param name="person">The person accepting, as Keycloak knows them.</param>
    /// <param name="name">The name the person goes by in Keycloak.</param>
    /// <param name="email">The address Keycloak verified for the person; it must be the invited one.</param>
    /// <param name="now">The moment of accepting.</param>
    public Result<Unit, AcceptInvitationError> AcceptInvitation(
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
        if (memberships.SingleOrDefault(m => m.Status == MembershipStatus.Invited && m.Holds(hash)) is not { } invited)
        {
            return Failure(PharmacyErrors.InvitationNotFound(Id));
        }

        if (invited.InvitationExpiresAt! < now)
        {
            return Failure(PharmacyErrors.AcceptedAfterExpiry(Id, invited.Id));
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
        Raise(new MembershipChanged(Id, membership, active.Person!, active.Name!, jobFunction, role, active.Version));
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

    /// <summary>
    /// The stored day of the invitation mail tally when it lies beyond tomorrow (a clock days ahead, a hand-edited row);
    /// none otherwise. Such a day counts as tomorrow and keeps its tally; the caller reports it, since only a clock fault
    /// puts it there.
    /// </summary>
    public Option<UtcDay> InvitationMailDayBeyondTomorrow(Timestamp now)
    {
        Ensure.That(now).IsNotNull();

        var tomorrow = UtcDay.Of(now).Next();
        if (InvitationMailDay is { } stored && stored.Value > tomorrow.Value)
        {
            return Option<UtcDay>.Some(stored);
        }

        return Option<UtcDay>.None;
    }

    private Membership? Find(MembershipId id) => memberships.SingleOrDefault(m => m.Id == id);

    /// <summary>The start of the day after the counted one when its mails have reached the cap; none while another may be sent.</summary>
    private Option<Timestamp> DailyCapReached(Timestamp now, DailyMailCap cap)
    {
        var day = CountingDay(now);
        if (CountedOn(day, now) && InvitationMailsThatDay.Value >= cap.Value)
        {
            return Option<Timestamp>.Some(day.Next().Start);
        }

        return Option<Timestamp>.None;
    }

    /// <summary>Counts one invitation mail toward the counted day's tally: the first of a new day starts it afresh.</summary>
    private void CountInvitationMail(Timestamp now)
    {
        var day = CountingDay(now);
        InvitationMailsThatDay = CountedOn(day, now) ? InvitationMailsThatDay.Next() : InvitationMailCount.Zero.Next();
        InvitationMailDay = day;
    }

    /// <summary>Whether the stored tally belongs to the counted day.</summary>
    private bool CountedOn(UtcDay day, Timestamp now)
    {
        var stored = StoredDayWithinTomorrow(now);
        return stored.HasValue && stored.Value == day;
    }

    /// <summary>
    /// The day a mail requested now counts on: today, or the stored day when it is later. A stored day of tomorrow is the
    /// counted day, so an instance whose clock is behind around 00:00 UTC adds to the new day's tally instead of
    /// restarting the old day's; a stored day further ahead (a clock days ahead, a hand-edited row) counts as tomorrow
    /// and keeps its tally, so the cap never restarts from it: at worst it locks invitation mail until the day after
    /// tomorrow.
    /// </summary>
    private UtcDay CountingDay(Timestamp now)
    {
        var today = UtcDay.Of(now);
        var stored = StoredDayWithinTomorrow(now);
        if (stored.HasValue && stored.Value.Value > today.Value)
        {
            return stored.Value;
        }

        return today;
    }

    /// <summary>The stored day, with any day beyond tomorrow taken as tomorrow; none before anything was counted.</summary>
    private Option<UtcDay> StoredDayWithinTomorrow(Timestamp now)
    {
        if (InvitationMailDay is not { } stored)
        {
            return Option<UtcDay>.None;
        }

        var tomorrow = UtcDay.Of(now).Next();
        if (stored.Value > tomorrow.Value)
        {
            return Option<UtcDay>.Some(tomorrow);
        }

        return Option<UtcDay>.Some(stored);
    }
}
