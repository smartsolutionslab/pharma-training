using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// Each invite and each renewal requests one invitation mail, and the pharmacy limits them: an invitation is renewed
/// at most once per cooldown (10 minutes by default), counted from its latest mail request, and a pharmacy requests
/// at most so many mails per UTC calendar day (50 by default). A refusal changes nothing, raises nothing and counts
/// toward neither limit; issuing, withdrawing and recording links count toward neither.
/// </summary>
public class InvitationMailLimitTests
{
    private const string RenewedTooSoon = "organisation.invitation-renewed-too-soon";
    private const string TooManyToday = "organisation.too-many-invitations-today";

    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextMidnight = new(2027, 4, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly InvitationMailLimits CapOfThree = new(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(3));

    [Fact]
    public void A_renewal_within_the_cooldown_is_refused_with_the_instant_it_may_be_renewed()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = Invite(pharmacy, "anna@example.org", Start);

        var refused = pharmacy.RenewInvitation(invitation, At(Start.AddMinutes(10).AddSeconds(-1)), InvitationMailLimits.Default);

        refused.IsSuccess.ShouldBeFalse("an invitation was renewed 9:59 minutes after its invite");
        refused.Error.Code.ShouldBe(RenewedTooSoon);
        var tooSoon = refused.Error.ShouldBeOfType<InvitationRenewedTooSoon>();
        tooSoon.Pharmacy.ShouldBe(pharmacy.Id);
        tooSoon.Membership.ShouldBe(invitation);
        tooSoon.RetryAt.ShouldBe(At(Start.AddMinutes(10)));
    }

    [Fact]
    public void A_renewal_once_the_cooldown_has_passed_is_accepted()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = Invite(pharmacy, "anna@example.org", Start);

        var renewed = pharmacy.RenewInvitation(invitation, At(Start.AddMinutes(10)), InvitationMailLimits.Default);

        renewed.IsSuccess.ShouldBeTrue("an invitation was refused a renewal 10 minutes after its invite");
        pharmacy.Membership(invitation).InvitationRequestedAt.ShouldBe(At(Start.AddMinutes(10)));
        pharmacy.Membership(invitation).InvitationVersion.ShouldBe(InvitationVersion.From(2));
    }

    [Fact]
    public void The_cooldown_runs_from_the_latest_renewal_not_the_invite()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = Invite(pharmacy, "anna@example.org", Start);
        pharmacy.RenewInvitation(invitation, At(Start.AddMinutes(10)), InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();

        var refused = pharmacy.RenewInvitation(invitation, At(Start.AddMinutes(15)), InvitationMailLimits.Default);
        var renewed = pharmacy.RenewInvitation(invitation, At(Start.AddMinutes(20)), InvitationMailLimits.Default);

        refused.IsSuccess.ShouldBeFalse("an invitation was renewed 5 minutes after its last renewal");
        refused.Error.ShouldBeOfType<InvitationRenewedTooSoon>().RetryAt.ShouldBe(At(Start.AddMinutes(20)));
        renewed.IsSuccess.ShouldBeTrue("an invitation was refused a renewal 10 minutes after its last renewal");
    }

    [Fact]
    public void A_refused_renewal_changes_nothing_and_raises_nothing()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = Invite(pharmacy, "anna@example.org", Start);
        pharmacy.IssueInvitationLink(invitation, Token(), At(Start)).IsSuccess.ShouldBeTrue();
        pharmacy.ClearDomainEvents();
        var membership = pharmacy.Membership(invitation);
        var version = membership.InvitationVersion;
        var links = membership.InvitationLinks.ToList();
        var expiresAt = membership.InvitationExpiresAt;
        var requestedAt = membership.InvitationRequestedAt;
        var day = pharmacy.InvitationMailDay;
        var count = pharmacy.InvitationMailsThatDay;

        var refused = pharmacy.RenewInvitation(invitation, At(Start.AddMinutes(5)), InvitationMailLimits.Default);

        refused.IsSuccess.ShouldBeFalse();
        membership.InvitationVersion.ShouldBe(version);
        membership.InvitationLinks.ShouldBe(links);
        membership.InvitationExpiresAt.ShouldBe(expiresAt);
        membership.InvitationRequestedAt.ShouldBe(requestedAt);
        pharmacy.InvitationMailDay.ShouldBe(day);
        pharmacy.InvitationMailsThatDay.ShouldBe(count);
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_pharmacy_at_its_daily_cap_is_refused_until_the_next_utc_day()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        Invite(pharmacy, "anna@example.org", Start, CapOfThree);
        Invite(pharmacy, "ben@example.org", Start.AddHours(1), CapOfThree);
        Invite(pharmacy, "cleo@example.org", Start.AddHours(2), CapOfThree);
        pharmacy.ClearDomainEvents();
        var memberships = pharmacy.Memberships.Count;

        var refused = pharmacy.Invite(MembershipId.New(), EmailAddress.From("dora@example.org"), JobFunction.Pta, MembershipRole.Employee, At(Start.AddHours(3)), CapOfThree);

        refused.IsSuccess.ShouldBeFalse("a fourth invitation mail was requested on a day capped at three");
        refused.Error.Code.ShouldBe(TooManyToday);
        var tooMany = refused.Error.ShouldBeOfType<TooManyInvitationsToday>();
        tooMany.Pharmacy.ShouldBe(pharmacy.Id);
        tooMany.RetryAt.ShouldBe(At(NextMidnight));
        pharmacy.Memberships.Count.ShouldBe(memberships);
        pharmacy.InvitationMailsThatDay.Value.ShouldBe(3);
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void The_daily_cap_counts_invites_and_renewals_together()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var anna = Invite(pharmacy, "anna@example.org", Start, CapOfThree);
        var ben = Invite(pharmacy, "ben@example.org", Start, CapOfThree);
        pharmacy.RenewInvitation(anna, At(Start.AddMinutes(10)), CapOfThree).IsSuccess.ShouldBeTrue();

        var invite = pharmacy.Invite(MembershipId.New(), EmailAddress.From("cleo@example.org"), JobFunction.Pta, MembershipRole.Employee, At(Start.AddMinutes(11)), CapOfThree);
        var renewal = pharmacy.RenewInvitation(ben, At(Start.AddMinutes(20)), CapOfThree);

        invite.IsSuccess.ShouldBeFalse("a fourth mail was requested after two invites and a renewal on a day capped at three");
        invite.Error.Code.ShouldBe(TooManyToday);
        renewal.IsSuccess.ShouldBeFalse("a renewal past its cooldown was mailed beyond the day's cap");
        renewal.Error.Code.ShouldBe(TooManyToday);
        renewal.Error.ShouldBeOfType<TooManyInvitationsToday>().RetryAt.ShouldBe(At(NextMidnight));
        pharmacy.InvitationMailsThatDay.Value.ShouldBe(3);
    }

    [Fact]
    public void A_new_utc_day_starts_the_count_afresh()
    {
        var limits = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(2));
        var pharmacy = TestPharmacies.Register(Owner);
        Invite(pharmacy, "anna@example.org", NextMidnight.AddMinutes(-2), limits);
        Invite(pharmacy, "ben@example.org", NextMidnight.AddMinutes(-1), limits);

        var lastMinute = pharmacy.Invite(MembershipId.New(), EmailAddress.From("cleo@example.org"), JobFunction.Pta, MembershipRole.Employee, At(NextMidnight.AddSeconds(-1)), limits);
        var midnight = pharmacy.Invite(MembershipId.New(), EmailAddress.From("cleo@example.org"), JobFunction.Pta, MembershipRole.Employee, At(NextMidnight), limits);

        lastMinute.IsSuccess.ShouldBeFalse("a third mail was requested at 23:59:59 UTC on a day capped at two");
        lastMinute.Error.ShouldBeOfType<TooManyInvitationsToday>().RetryAt.ShouldBe(At(NextMidnight));
        midnight.IsSuccess.ShouldBeTrue("the next UTC day still counted the day before's mails");
        pharmacy.InvitationMailDay.ShouldBe(UtcDay.Of(At(NextMidnight)));
        pharmacy.InvitationMailsThatDay.Value.ShouldBe(1);
    }

    [Fact]
    public void A_stored_day_later_than_today_counts_as_today_so_a_clock_behind_does_not_restart_the_count()
    {
        var limits = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(2));
        var pharmacy = TestPharmacies.Register(Owner);
        Invite(pharmacy, "anna@example.org", NextMidnight.AddSeconds(30), limits);

        var behind = pharmacy.Invite(MembershipId.New(), EmailAddress.From("ben@example.org"), JobFunction.Pta, MembershipRole.Employee, At(NextMidnight.AddSeconds(-10)), limits);
        var counted = pharmacy.InvitationMailsThatDay.Value;
        var day = pharmacy.InvitationMailDay;
        var third = pharmacy.Invite(MembershipId.New(), EmailAddress.From("cleo@example.org"), JobFunction.Pta, MembershipRole.Employee, At(NextMidnight.AddSeconds(-5)), limits);

        behind.IsSuccess.ShouldBeTrue("the second mail of a day capped at two was refused");
        counted.ShouldBe(2, "a clock 40 seconds behind restarted the next day's count");
        day.ShouldBe(UtcDay.Of(At(NextMidnight)), "a clock behind moved the counted day back");
        third.IsSuccess.ShouldBeFalse("a third mail was requested on a day capped at two from a clock behind");
        third.Error.ShouldBeOfType<TooManyInvitationsToday>().RetryAt.ShouldBe(At(NextMidnight.AddDays(1)));
        pharmacy.InvitationMailsThatDay.Value.ShouldBe(2);
    }

    [Fact]
    public void A_stored_day_more_than_one_day_ahead_counts_as_today_and_restarts_the_count()
    {
        var limits = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(2));
        var pharmacy = TestPharmacies.Register(Owner);
        Invite(pharmacy, "anna@example.org", Start.AddDays(3), limits);

        var today = pharmacy.Invite(MembershipId.New(), EmailAddress.From("ben@example.org"), JobFunction.Pta, MembershipRole.Employee, At(Start), limits);

        today.IsSuccess.ShouldBeTrue("a mail was refused on a day that had counted none");
        pharmacy.InvitationMailDay.ShouldBe(UtcDay.Of(At(Start)), "a day three days ahead was kept as the counted day");
        pharmacy.InvitationMailsThatDay.Value.ShouldBe(1, "a day three days ahead was counted onto");
    }

    [Fact]
    public void A_stored_day_at_the_end_of_the_calendar_counts_as_today_and_does_not_throw()
    {
        var limits = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        var pharmacy = TestPharmacies.Register(Owner);
        Invite(pharmacy, "anna@example.org", Start, limits);
        typeof(Pharmacy).GetProperty(nameof(Pharmacy.InvitationMailDay)).ShouldNotBeNull().SetValue(pharmacy, UtcDay.From(DateOnly.MaxValue));

        var today = pharmacy.Invite(MembershipId.New(), EmailAddress.From("ben@example.org"), JobFunction.Pta, MembershipRole.Employee, At(Start.AddHours(1)), limits);

        today.IsSuccess.ShouldBeTrue("a stored day of 9999-12-31 kept today's mail from being counted on today");
        pharmacy.InvitationMailDay.ShouldBe(UtcDay.Of(At(Start)));
        pharmacy.InvitationMailsThatDay.Value.ShouldBe(1);
    }

    [Fact]
    public void An_invite_refused_as_already_invited_counts_toward_nothing()
    {
        var limits = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(2));
        var pharmacy = TestPharmacies.Register(Owner);
        Invite(pharmacy, "anna@example.org", Start, limits);

        var again = pharmacy.Invite(MembershipId.New(), EmailAddress.From("anna@example.org"), JobFunction.Pta, MembershipRole.Employee, At(Start.AddMinutes(1)), limits);
        var counted = pharmacy.InvitationMailsThatDay.Value;
        var ben = pharmacy.Invite(MembershipId.New(), EmailAddress.From("ben@example.org"), JobFunction.Pta, MembershipRole.Employee, At(Start.AddMinutes(2)), limits);

        again.IsSuccess.ShouldBeFalse();
        again.Error.Code.ShouldBe("organisation.already-member-or-invited");
        counted.ShouldBe(1, "the refused invite was counted");
        ben.IsSuccess.ShouldBeTrue("the second mail of a day capped at two was refused after a refused invite");
        pharmacy.InvitationMailsThatDay.Value.ShouldBe(2);
    }

    [Fact]
    public void Issuing_withdrawing_and_recording_links_counts_toward_neither_limit()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = Invite(pharmacy, "anna@example.org", Start);
        var withdrawn = Token();
        pharmacy.IssueInvitationLink(invitation, withdrawn, At(Start.AddMinutes(1))).IsSuccess.ShouldBeTrue();
        pharmacy.WithdrawInvitationLink(invitation, withdrawn).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(invitation, Token(), At(Start.AddMinutes(2))).IsSuccess.ShouldBeTrue();
        pharmacy.RecordInvitationMailed(invitation, pharmacy.Membership(invitation).InvitationVersion).IsSuccess.ShouldBeTrue();

        pharmacy.Membership(invitation).InvitationRequestedAt.ShouldBe(At(Start));
        pharmacy.InvitationMailDay.ShouldBe(UtcDay.Of(At(Start)));
        pharmacy.InvitationMailsThatDay.Value.ShouldBe(1);
        pharmacy.RenewInvitation(invitation, At(Start.AddMinutes(10)), InvitationMailLimits.Default).IsSuccess
            .ShouldBeTrue("the cooldown ran from a link issued after the invite, not from the invite");
    }

    [Fact]
    public void A_revoked_invitation_renewed_within_the_cooldown_is_still_no_open_invitation()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = Invite(pharmacy, "anna@example.org", Start);
        pharmacy.RevokeInvitation(invitation).IsSuccess.ShouldBeTrue();

        var refused = pharmacy.RenewInvitation(invitation, At(Start.AddMinutes(1)), InvitationMailLimits.Default);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.no-open-invitation");
    }

    [Fact]
    public void A_founder_is_never_mailed_so_a_registered_pharmacy_has_counted_nothing()
    {
        var pharmacy = TestPharmacies.Register(Owner);

        pharmacy.InvitationMailDay.ShouldBeNull();
        pharmacy.InvitationMailsThatDay.ShouldBe(InvitationMailCount.Zero);
        pharmacy.Memberships.ShouldHaveSingleItem().InvitationRequestedAt.ShouldBeNull();
    }

    [Fact]
    public void By_default_an_invitation_is_renewed_at_most_every_10_minutes_and_a_pharmacy_mails_at_most_50_a_day()
    {
        InvitationMailLimits.Default.RenewCooldown.Value.ShouldBe(TimeSpan.FromMinutes(10));
        InvitationMailLimits.Default.DailyMailCap.Value.ShouldBe(50);
    }

    private static Timestamp At(DateTimeOffset instant) => Timestamp.From(instant);

    private static MembershipId Invite(Pharmacy pharmacy, string email, DateTimeOffset at, InvitationMailLimits? limits = null)
    {
        var invited = pharmacy.Invite(MembershipId.New(), EmailAddress.From(email), JobFunction.Pta, MembershipRole.Employee, At(at), limits ?? InvitationMailLimits.Default);
        invited.IsSuccess.ShouldBeTrue($"inviting {email} at {at:O} was refused");
        return invited.Value;
    }

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));
}
