// Organisation service — Domain (module.style: ddd, module.ddd-depth: strict)
// One file for review; in the project every type lives in its own file under Domain/Pharmacies/.
// Identities (PharmacyId, MembershipId, PersonId) and Timestamp: same shape as in Compliance.Domain.cs;
// this service's Timestamp adds PlusDays(int days) => From(Value.AddDays(days)) and Plus(TimeSpan span) => From(Value.Add(span)).

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

// ───────────────────────────── Values ─────────────────────────────

public sealed record PharmacyName : IValueObject<string>
{
    public const int MaxLength = 120;

    private PharmacyName(string value) => Value = value;

    public string Value { get; }

    public static PharmacyName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new PharmacyName(value.Trim());
    }
}

/// <summary>The name a person goes by, as Keycloak has it. The person keeps it there; the manager never types it.</summary>
public sealed record DisplayName : IValueObject<string>
{
    public const int MaxLength = 100;

    private DisplayName(string value) => Value = value;

    public string Value { get; }

    public static DisplayName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new DisplayName(value.Trim());
    }
}

/// <summary>An e-mail address, stored lower-case so that two spellings of one address are equal.</summary>
public sealed partial record EmailAddress : IValueObject<string>
{
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Matches(Shape(), "Keine gültige E-Mail-Adresse.");
        return new EmailAddress(value.Trim().ToLowerInvariant());
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Shape();
}

/// <summary>The secret in an invitation link. Created by the Application from a cryptographic random source.</summary>
public sealed record InvitationToken : IValueObject<string>
{
    public const int MinLength = 32;

    private InvitationToken(string value) => Value = value;

    public string Value { get; }

    public static InvitationToken From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMinLength(MinLength);
        return new InvitationToken(value);
    }

    /// <summary>Never the secret itself, so no log or record print shows it; read <see cref="Value"/> for the link.</summary>
    public override string ToString() => "InvitationToken { *** }";
}

/// <summary>The SHA-256 of an invitation token in lower-case hex, the only form in which the token is stored.</summary>
public sealed partial record InvitationTokenHash : IValueObject<string>
{
    public const int Length = 64;

    private InvitationTokenHash(string value) => Value = value;

    public string Value { get; }

    public static InvitationTokenHash From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().Matches(Shape(), "Kein SHA-256 in Kleinbuchstaben-Hex.");
        return new InvitationTokenHash(value);
    }

    public static InvitationTokenHash Of(InvitationToken token) =>
        new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token.Value))));

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Shape();
}

/// <summary>Which version of its invitation a membership holds, counted from 1 at the invite; each renewal is the next.</summary>
public sealed record InvitationVersion : IValueObject<int>
{
    private InvitationVersion(int value) => Value = value;

    public int Value { get; }

    public static InvitationVersion First => new(1);

    public static InvitationVersion From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new InvitationVersion(value);
    }

    public InvitationVersion Next() => new(Value + 1);
}

/// <summary>Which state of a membership this is, counted from 1 at its start; each change of job function or role is the next.</summary>
public sealed record MembershipVersion : IValueObject<int>
{
    private MembershipVersion(int value) => Value = value;

    public int Value { get; }

    public static MembershipVersion First => new(1);

    public static MembershipVersion From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new MembershipVersion(value);
    }

    public MembershipVersion Next() => new(Value + 1);
}

/// <summary>A UTC calendar day, from 00:00 UTC to the next; the day a pharmacy's invitation mails are counted in.</summary>
public sealed record UtcDay : IValueObject<DateOnly>
{
    private UtcDay(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public Timestamp Start => Timestamp.From(new DateTimeOffset(Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    public static UtcDay From(DateOnly value) => new(value);

    public static UtcDay Of(Timestamp instant) => new(DateOnly.FromDateTime(instant.Value.UtcDateTime));

    public UtcDay Next() => new(Value.AddDays(1));
}

/// <summary>How many invitation mails a pharmacy has requested on one UTC day.</summary>
public sealed record InvitationMailCount : IValueObject<int>
{
    private InvitationMailCount(int value) => Value = value;

    public int Value { get; }

    public static InvitationMailCount Zero => new(0);

    public static InvitationMailCount From(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new InvitationMailCount(value);
    }

    public InvitationMailCount Next() => new(Value + 1);
}

/// <summary>How long after its latest mail request an invitation may be renewed again: more than nothing, at most a day.</summary>
public sealed record RenewCooldown : IValueObject<TimeSpan>
{
    private RenewCooldown(TimeSpan value) => Value = value;

    public TimeSpan Value { get; }

    public static RenewCooldown From(TimeSpan value)
    {
        Ensure.That((decimal)value.Ticks).Satisfies(ticks => ticks > 0 && ticks <= TimeSpan.TicksPerDay, $"a renew cooldown is more than nothing and at most a day; got {value}.");
        return new RenewCooldown(value);
    }
}

/// <summary>How many invitation mails a pharmacy may request on one UTC day: from 1 to 10000.</summary>
public sealed record DailyMailCap : IValueObject<int>
{
    public const int Maximum = 10000;

    private DailyMailCap(int value) => Value = value;

    public int Value { get; }

    public static DailyMailCap From(int value)
    {
        Ensure.That(value).InRange(1, Maximum);
        return new DailyMailCap(value);
    }
}

/// <summary>The host's limits on invitation mails (docs/adr/0004); <see cref="Default"/> is 10 minutes and 50 mails.</summary>
public sealed record InvitationMailLimits(RenewCooldown RenewCooldown, DailyMailCap DailyMailCap)
{
    public const int DefaultRenewCooldownMinutes = 10;

    public const int DefaultMailsPerPharmacyPerDay = 50;

    public static InvitationMailLimits Default { get; } = new(
        RenewCooldown.From(TimeSpan.FromMinutes(DefaultRenewCooldownMinutes)),
        DailyMailCap.From(DefaultMailsPerPharmacyPerDay));
}

/// <summary>One link issued for the open invitation's current version, kept as its token's hash only.</summary>
public sealed class InvitationLink
{
    private InvitationLink(InvitationTokenHash hash) => Hash = hash;

    public InvitationTokenHash Hash { get; }

    internal static InvitationLink Of(InvitationTokenHash hash) => new(hash);
}

public enum MembershipRole
{
    Manager,
    Employee,
}

/// <summary>What someone does in the pharmacy. PTA and PKA stay German: they are terms of the trade.</summary>
public enum JobFunction
{
    Pharmacist,
    Pta,
    Pka,
    Trainee,
    Other,
}

public enum MembershipStatus
{
    Invited,
    Active,
    Ended,
    Revoked,
}

/// <summary>What a role allows. Endpoints ask for a capability, never for a role, so new roles need no endpoint change.</summary>
public enum Capability
{
    ManageMembers,
    AssignCourses,
}

public static class RoleCapabilities
{
    public static bool Grants(this MembershipRole role, Capability capability) =>
        role == MembershipRole.Manager; // MVP 1: the manager can do everything, an employee nothing administrative
}

// ───────────────────────────── Entity ─────────────────────────────

/// <summary>A person's place in one pharmacy, from the invitation to the day they leave.</summary>
public sealed class Membership : Entity<MembershipId>
{
    private readonly List<InvitationLink> invitationLinks = [];

    private Membership(MembershipId id)
        : base(id)
    {
    }

    public EmailAddress Email { get; private set; } = default!;

    /// <summary>The links issued for the current version; each opens the invitation, since the invitee may hold any of their mails.</summary>
    public IReadOnlyList<InvitationLink> InvitationLinks => invitationLinks;

    /// <summary>1 at the invite, the next at each renewal; a founder keeps 1 and is never mailed.</summary>
    public InvitationVersion InvitationVersion { get; private set; } = InvitationVersion.First;

    /// <summary>1 from the start, the next at each change of job function or role (docs/adr/0015).</summary>
    public MembershipVersion Version { get; private set; } = MembershipVersion.First;

    /// <summary>The version whose mail was sent; a version that is mailed is not mailed again.</summary>
    public InvitationVersion? InvitationMailedFor { get; private set; }

    /// <summary>When the newest invitation mail was requested, by the invite or a renewal; none for a founder.</summary>
    public Timestamp? InvitationRequestedAt { get; private set; }

    public Timestamp? InvitationExpiresAt { get; private set; }

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

    internal bool Holds(InvitationTokenHash tokenHash) => invitationLinks.Any(link => link.Hash == tokenHash);

    internal bool HoldsMostLinks() => invitationLinks.Count >= Pharmacy.MaxInvitationLinksPerVersion;

    /// <summary>A link mailed now, beside the version's earlier ones, which keep working.</summary>
    internal void IssueLink(InvitationTokenHash tokenHash) => invitationLinks.Add(InvitationLink.Of(tokenHash));

    /// <summary>Takes back the link whose mail never left; answers whether the version held it.</summary>
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

// ───────────────────────────── Aggregate ─────────────────────────────

/// <summary>
/// A pharmacy with its memberships. They live inside the aggregate because two rules need all of them at once:
/// there is always at least one active manager, and a person or address is active or invited at most once.
/// </summary>
public sealed class Pharmacy : AggregateRoot<PharmacyId>
{
    /// <summary>How long an invitation link works.</summary>
    public const int InvitationValidityDays = 14;

    /// <summary>How many links one invitation version may hold whose mails may be out; a renewal clears them.</summary>
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
        var pharmacy = new Pharmacy(id, name, now);
        var membership = Membership.Founder(founderMembership, founderEmail, founder, founderName, founderJobFunction, now);
        pharmacy.memberships.Add(membership);
        pharmacy.Raise(new PharmacyRegistered(id, name));
        pharmacy.Raise(new MembershipStarted(id, membership.Id, founder, founderName, founderJobFunction, MembershipRole.Manager));
        return pharmacy;
    }

    /// <summary>
    /// Invites the address for 14 days; it holds no link until its mail issues one (IssueInvitationLink). Its mail counts
    /// toward the pharmacy's daily cap, which refuses it once reached.
    /// </summary>
    public Result<MembershipId, DomainError> Invite(MembershipId id, EmailAddress email, JobFunction jobFunction, MembershipRole role, Timestamp now, InvitationMailLimits limits)
    {
        if (memberships.Any(m => m.Email == email && m.Status is MembershipStatus.Invited or MembershipStatus.Active))
        {
            return Failure(new AlreadyMemberOrInvited(Id, email));
        }

        if (DailyCapReached(now, limits.DailyMailCap) is Some<Timestamp> tomorrow)
        {
            return Failure(new TooManyInvitationsToday(Id, tomorrow.Value));
        }

        var expiresAt = now.PlusDays(InvitationValidityDays);
        CountInvitationMail(now);
        memberships.Add(Membership.Invited(id, email, jobFunction, role, now, expiresAt));
        Raise(new MemberInvited(Id, id, expiresAt));
        return Success(id);
    }

    /// <summary>
    /// Renews an open or expired invitation for 14 days from now, as its next version; the old links stop working at once.
    /// Refused within the cooldown since the invitation's latest mail request, and once the pharmacy's daily cap is reached.
    /// </summary>
    public Result<Unit, DomainError> RenewInvitation(MembershipId membership, Timestamp now, InvitationMailLimits limits)
    {
        if (Find(membership) is not { Status: MembershipStatus.Invited } invited)
        {
            return Failure(new NoOpenInvitation(Id, membership));
        }

        if (invited.RenewableFrom(limits.RenewCooldown) is Some<Timestamp> renewableFrom && now < renewableFrom.Value)
        {
            return Failure(new InvitationRenewedTooSoon(Id, membership, renewableFrom.Value));
        }

        if (DailyCapReached(now, limits.DailyMailCap) is Some<Timestamp> tomorrow)
        {
            return Failure(new TooManyInvitationsToday(Id, tomorrow.Value));
        }

        var expiresAt = now.PlusDays(InvitationValidityDays);
        CountInvitationMail(now);
        invited.RenewInvitation(now, expiresAt);
        Raise(new MemberInvited(Id, membership, expiresAt));
        return Success(Unit.Value);
    }

    /// <summary>
    /// Issues a link of an open invitation that has not run out, when its mail is sent: the token's hash joins the
    /// version's earlier ones, which keep working, up to MaxInvitationLinksPerVersion. Answers its expiry.
    /// </summary>
    public Result<Timestamp, DomainError> IssueInvitationLink(MembershipId membership, InvitationToken token, Timestamp now)
    {
        if (Find(membership) is not { Status: MembershipStatus.Invited, InvitationExpiresAt: { } expiresAt } invited)
        {
            return Failure(new NoOpenInvitation(Id, membership));
        }

        if (expiresAt < now)
        {
            return Failure(new InvitationExpired(Id, membership));
        }

        if (invited.HoldsMostLinks())
        {
            return Failure(new InvitationLinkLimitReached(Id, membership));
        }

        invited.IssueLink(InvitationTokenHash.Of(token));
        Raise(new InvitationLinkIssued(Id, membership));
        return Success(expiresAt);
    }

    /// <summary>Takes back a link whose mail never reached the mail server; one the invitation no longer holds is already gone.</summary>
    public Result<Unit, DomainError> WithdrawInvitationLink(MembershipId membership, InvitationToken token)
    {
        if (Find(membership) is not { Status: MembershipStatus.Invited } invited)
        {
            return Failure(new NoOpenInvitation(Id, membership));
        }

        if (invited.WithdrawLink(InvitationTokenHash.Of(token)))
        {
            Raise(new InvitationLinkWithdrawn(Id, membership));
        }

        return Success(Unit.Value);
    }

    /// <summary>Records that the open version's mail was sent, so a redelivery does not mail it again.</summary>
    public Result<Unit, DomainError> RecordInvitationMailed(MembershipId membership, InvitationVersion version)
    {
        if (Find(membership) is not { Status: MembershipStatus.Invited } invited || invited.InvitationVersion != version)
        {
            return Failure(new NoOpenInvitation(Id, membership));
        }

        invited.RecordMailed(version);
        Raise(new InvitationMailed(Id, membership, version));
        return Success(Unit.Value);
    }

    /// <param name="email">The address Keycloak verified for the person; it must be the invited one.</param>
    public Result<Unit, DomainError> AcceptInvitation(
        InvitationToken token,
        PersonId person,
        DisplayName name,
        EmailAddress email,
        Timestamp now)
    {
        if (memberships.SingleOrDefault(m => m.Status == MembershipStatus.Invited && m.Holds(InvitationTokenHash.Of(token))) is not { } invited)
        {
            return Failure(new InvitationNotFound(Id));
        }

        if (invited.InvitationExpiresAt!.CompareTo(now) < 0)
        {
            return Failure(new InvitationExpired(Id, invited.Id));
        }

        if (invited.Email != email)
        {
            return Failure(new InvitationForAnotherAddress(Id, invited.Id));
        }

        if (memberships.Any(m => m.Person == person && m.Status == MembershipStatus.Active))
        {
            return Failure(new PersonAlreadyMember(Id, person));
        }

        invited.Activate(person, name, now);
        Raise(new MembershipStarted(Id, invited.Id, person, name, invited.JobFunction, invited.Role));
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> RevokeInvitation(MembershipId membership)
    {
        if (Find(membership) is not { Status: MembershipStatus.Invited } invited)
        {
            return Failure(new NoOpenInvitation(Id, membership));
        }

        invited.Revoke();
        Raise(new InvitationRevoked(Id, membership));
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> EndMembership(MembershipId membership, Timestamp now)
    {
        if (Find(membership) is not { Status: MembershipStatus.Active } active)
        {
            return Failure(new NoActiveMembership(Id, membership));
        }

        if (active.IsActiveManager && memberships.Count(m => m.IsActiveManager) == 1)
        {
            return Failure(new LastManagerCannotLeave(Id));
        }

        active.End(now);
        Raise(new MembershipEnded(Id, membership, active.Person!, now));
        return Success(Unit.Value);
    }

    public Result<Unit, DomainError> ChangeMembership(MembershipId membership, JobFunction jobFunction, MembershipRole role)
    {
        if (Find(membership) is not { Status: MembershipStatus.Active } active)
        {
            return Failure(new NoActiveMembership(Id, membership));
        }

        if (active.IsActiveManager && role != MembershipRole.Manager && memberships.Count(m => m.IsActiveManager) == 1)
        {
            return Failure(new LastManagerCannotLeave(Id));
        }

        if (active.JobFunction == jobFunction && active.Role == role)
        {
            return Success(Unit.Value);
        }

        active.Change(jobFunction, role);
        Raise(new MembershipChanged(Id, membership, active.Person!, active.Name!, jobFunction, role, active.Version));
        return Success(Unit.Value);
    }

    public Option<Membership> ActiveMembershipOf(PersonId person) =>
        memberships.SingleOrDefault(m => m.Person == person && m.Status == MembershipStatus.Active) is { } found
            ? Option<Membership>.Some(found)
            : Option<Membership>.None;

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

// ───────────────────────────── Domain events ─────────────────────────────
// PharmacyRegistered, MembershipStarted, MembershipChanged, MembershipEnded → integration events V1 (Compliance, Certification).
// MemberInvited → the command publishes InvitationMailRequestedV1; its own consumer issues the link (IssueInvitationLink) and mails it (docs/adr/0004).

public sealed record PharmacyRegistered(PharmacyId Pharmacy, PharmacyName Name) : IDomainEvent;

/// <summary>Carries no token, address or name.</summary>
[DeliberatelyUnhandled("The command that invites or renews publishes InvitationMailRequestedV1; its consumer issues the link and mails it.")]
public sealed record MemberInvited(PharmacyId Pharmacy, MembershipId Membership, Timestamp ExpiresAt) : IDomainEvent;

[DeliberatelyUnhandled("The consumer that issued the link mails it; nothing reacts.")]
public sealed record InvitationLinkIssued(PharmacyId Pharmacy, MembershipId Membership) : IDomainEvent;

/// <summary>A link whose mail never reached the mail server was taken back. Carries no token.</summary>
[DeliberatelyUnhandled("The consumer that issued the link withdraws it and retries; nothing reacts.")]
public sealed record InvitationLinkWithdrawn(PharmacyId Pharmacy, MembershipId Membership) : IDomainEvent;

/// <summary>The mail of an invitation's version was handed to the mail server; that version is not mailed again.</summary>
[DeliberatelyUnhandled("The consumer that sent the mail records it; nothing reacts.")]
public sealed record InvitationMailed(PharmacyId Pharmacy, MembershipId Membership, InvitationVersion Version) : IDomainEvent;

public sealed record MembershipStarted(PharmacyId Pharmacy, MembershipId Membership, PersonId Person, DisplayName Name, JobFunction JobFunction, MembershipRole Role) : IDomainEvent;

[DeliberatelyUnhandled("The open invitation simply disappears from the list; nobody outside Organisation cares.")]
public sealed record InvitationRevoked(PharmacyId Pharmacy, MembershipId Membership) : IDomainEvent;

public sealed record MembershipEnded(PharmacyId Pharmacy, MembershipId Membership, PersonId Person, Timestamp EndedAt) : IDomainEvent;

[DeliberatelyUnhandled("The command that changes the membership announces it to other modules as MembershipChangedV1; nothing in Organisation reacts.")]
public sealed record MembershipChanged(PharmacyId Pharmacy, MembershipId Membership, PersonId Person, DisplayName Name, JobFunction JobFunction, MembershipRole Role, MembershipVersion Version) : IDomainEvent;

// ───────────────────────────── Domain errors ─────────────────────────────

public sealed record AlreadyMemberOrInvited(PharmacyId Pharmacy, EmailAddress Email)
    : DomainError("organisation.already-member-or-invited", "Diese Adresse ist schon Mitglied oder eingeladen.");

public sealed record NoOpenInvitation(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.no-open-invitation", "Es gibt keine offene Einladung.");

public sealed record InvitationNotFound(PharmacyId Pharmacy)
    : DomainError("organisation.invitation-not-found", "Der Einladungslink ist ungültig oder wurde schon benutzt.");

public sealed record InvitationExpired(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.invitation-expired", "Die Einladung ist abgelaufen. Bitte eine neue anfordern.");

public sealed record InvitationLinkLimitReached(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.invitation-link-limit-reached", "The invitation already holds as many links as one invitation version may; renew it to issue a new one.");

public sealed record InvitationRenewedTooSoon(PharmacyId Pharmacy, MembershipId Membership, Timestamp RetryAt)
    : DomainError("organisation.invitation-renewed-too-soon", "The invitation was mailed a moment ago; renew it once the waiting time has passed.");

public sealed record TooManyInvitationsToday(PharmacyId Pharmacy, Timestamp RetryAt)
    : DomainError("organisation.too-many-invitations-today", "The pharmacy has sent as many invitation mails today as it may; try again later.");

public sealed record InvitationForAnotherAddress(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.invitation-for-another-address", "Die Einladung gilt für eine andere E-Mail-Adresse.");

public sealed record PersonAlreadyMember(PharmacyId Pharmacy, PersonId Person)
    : DomainError("organisation.person-already-member", "Diese Person ist bereits Mitglied der Apotheke.");

public sealed record NoActiveMembership(PharmacyId Pharmacy, MembershipId Membership)
    : DomainError("organisation.no-active-membership", "Es gibt keine aktive Mitgliedschaft.");

public sealed record LastManagerCannotLeave(PharmacyId Pharmacy)
    : DomainError("organisation.last-manager", "Die letzte Apothekenleitung kann nicht entfernt oder herabgestuft werden.");
