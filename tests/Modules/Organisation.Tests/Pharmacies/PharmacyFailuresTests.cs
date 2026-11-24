using System.Diagnostics;
using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>What managing members refuses, each with its own code and status, and the domain's refusals mapped to theirs.</summary>
public class PharmacyFailuresTests
{
    private static readonly PharmacyId Pharmacy = PharmacyId.New();

    [Fact]
    public void A_membership_that_is_not_active_is_not_found()
    {
        var mapped = PharmacyFailures.From(PharmacyErrors.NoActiveMembership(Pharmacy, MembershipId.New()));

        mapped.Code.ShouldBe("organisation.no-active-membership");
        mapped.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Removing_the_last_manager_is_a_conflict()
    {
        var mapped = PharmacyFailures.From(PharmacyErrors.LastManagerCannotLeave(Pharmacy));

        mapped.Code.ShouldBe("organisation.last-manager");
        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Inviting_an_address_that_is_invited_or_active_is_a_conflict_with_the_domains_code()
    {
        var mapped = PharmacyFailures.From(PharmacyErrors.AlreadyMemberOrInvited(Pharmacy, EmailAddress.From("anna@example.org")));

        mapped.Code.ShouldBe("organisation.already-member-or-invited");
        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_membership_that_is_no_open_invitation_is_not_found_with_the_domains_code()
    {
        var mapped = PharmacyFailures.From(PharmacyErrors.NoOpenInvitation(Pharmacy, MembershipId.New()));

        mapped.Code.ShouldBe("organisation.no-open-invitation");
        mapped.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void An_address_that_is_no_address_is_the_callers_mistake()
    {
        PharmacyFailures.InvalidEmail().Code.ShouldBe("organisation.invalid-email");
        PharmacyFailures.InvalidEmail().Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void A_domain_error_no_use_case_returns_yet_is_a_bug_and_never_given_a_guessed_status() =>
        Should.Throw<UnreachableException>(() => PharmacyFailures.From(new UnmappedDomainError()))
            .Message.ShouldContain("organisation.test-unmapped");

    [Fact]
    public void A_link_no_invitation_holds_is_not_found_with_the_domains_code()
    {
        var mapped = PharmacyFailures.From(PharmacyErrors.InvitationNotFound(Pharmacy));

        mapped.Code.ShouldBe("organisation.invitation-not-found");
        mapped.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void A_link_no_pharmacy_holds_answers_exactly_as_a_link_the_pharmacy_does_not_hold_so_the_two_cannot_be_told_apart()
    {
        var unknown = PharmacyFailures.InvitationLinkNotFound();
        var mapped = PharmacyFailures.From(PharmacyErrors.InvitationNotFound(Pharmacy));

        unknown.Code.ShouldBe("organisation.invitation-not-found");
        unknown.Code.ShouldBe(mapped.Code);
        unknown.Message.ShouldBe(mapped.Message);
        unknown.Status.ShouldBe(mapped.Status);
        unknown.Message.ShouldNotContain(Pharmacy.Value.ToString());
        mapped.Message.ShouldNotContain(Pharmacy.Value.ToString());
    }

    [Fact]
    public void An_invitation_that_ran_out_is_gone_with_the_domains_code()
    {
        var mapped = PharmacyFailures.From(PharmacyErrors.InvitationExpired(Pharmacy, MembershipId.New()));

        mapped.Code.ShouldBe("organisation.invitation-expired");
        mapped.Status.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public void A_link_for_another_address_is_forbidden_with_the_domains_code()
    {
        var mapped = PharmacyFailures.From(PharmacyErrors.InvitationForAnotherAddress(Pharmacy, MembershipId.New()));

        mapped.Code.ShouldBe("organisation.invitation-for-another-address");
        mapped.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void Accepting_as_a_person_already_active_in_the_pharmacy_is_a_conflict_with_the_domains_code()
    {
        var mapped = PharmacyFailures.From(PharmacyErrors.PersonAlreadyMember(Pharmacy, PersonId.From(Guid.CreateVersion7())));

        mapped.Code.ShouldBe("organisation.person-already-member");
        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void An_account_whose_address_is_not_verified_is_forbidden()
    {
        PharmacyFailures.EmailNotVerified().Code.ShouldBe("organisation.email-not-verified");
        PharmacyFailures.EmailNotVerified().Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void An_invitation_that_changed_while_it_was_accepted_is_a_conflict_that_names_no_pharmacy()
    {
        var changed = PharmacyFailures.InvitationChanged();

        changed.Code.ShouldBe("organisation.invitation-changed");
        changed.Status.ShouldBe(HttpStatusCode.Conflict);
        changed.Message.ShouldNotBeNullOrWhiteSpace();
        changed.ShouldBe(PharmacyFailures.InvitationChanged());
    }

    [Fact]
    public void A_token_that_is_no_token_is_the_callers_mistake()
    {
        PharmacyFailures.InvalidInvitationToken().Code.ShouldBe("organisation.invalid-invitation-token");
        PharmacyFailures.InvalidInvitationToken().Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void A_membership_id_that_names_no_membership_is_not_found_with_the_domains_code()
    {
        PharmacyFailures.MembershipNotFound(Guid.Empty).Code.ShouldBe("organisation.no-active-membership");
        PharmacyFailures.MembershipNotFound(Guid.Empty).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void A_membership_id_that_names_no_open_invitation_is_not_found_with_the_domains_code()
    {
        PharmacyFailures.OpenInvitationNotFound(Guid.Empty).Code.ShouldBe("organisation.no-open-invitation");
        PharmacyFailures.OpenInvitationNotFound(Guid.Empty).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void A_membership_role_that_is_no_known_one_is_the_callers_mistake()
    {
        PharmacyFailures.InvalidMembershipRole().Code.ShouldBe("organisation.invalid-membership-role");
        PharmacyFailures.InvalidMembershipRole().Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void The_cooldown_and_the_daily_cap_map_to_429_retry_later_with_their_instants()
    {
        var renewAt = Timestamp.From(new DateTimeOffset(2027, 4, 1, 8, 10, 0, TimeSpan.Zero));
        var tomorrow = Timestamp.From(new DateTimeOffset(2027, 4, 2, 0, 0, 0, TimeSpan.Zero));
        var refusedAt = Timestamp.From(new DateTimeOffset(2027, 4, 1, 8, 4, 0, TimeSpan.Zero));
        var membership = MembershipId.New();

        var tooSoon = PharmacyFailures.From(PharmacyErrors.InvitationRenewedTooSoon(Pharmacy, membership, renewAt, refusedAt));
        var tooMany = PharmacyFailures.From(PharmacyErrors.TooManyInvitationsToday(Pharmacy, tomorrow, refusedAt));

        tooSoon.Code.ShouldBe("organisation.invitation-renewed-too-soon");
        tooSoon.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        tooSoon.Message.ShouldNotBeNullOrWhiteSpace();
        tooSoon.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(renewAt.Value);
        tooSoon.ShouldBeOfType<RetryLaterFailure>().RefusedAt.ShouldBe(Option<DateTimeOffset>.Some(refusedAt.Value));
        tooMany.Code.ShouldBe("organisation.too-many-invitations-today");
        tooMany.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        tooMany.Message.ShouldNotBeNullOrWhiteSpace();
        tooMany.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(tomorrow.Value);
        tooMany.ShouldBeOfType<RetryLaterFailure>().RefusedAt.ShouldBe(Option<DateTimeOffset>.Some(refusedAt.Value));
    }

    [Fact]
    public void The_recipient_cooldown_and_the_sender_cap_map_to_429_retry_later_with_their_instants()
    {
        var mailAgainAt = Timestamp.From(new DateTimeOffset(2027, 4, 1, 8, 10, 0, TimeSpan.Zero));
        var tomorrow = Timestamp.From(new DateTimeOffset(2027, 4, 2, 0, 0, 0, TimeSpan.Zero));
        var address = EmailAddress.From("anna@example.org");
        var refusedAt = Timestamp.From(new DateTimeOffset(2027, 4, 1, 8, 4, 0, TimeSpan.Zero));
        var sender = PersonId.From(Guid.CreateVersion7());

        var recently = PharmacyFailures.From(InvitationRecipientErrors.MailedRecently(InvitationRecipientId.For(address), mailAgainAt, refusedAt));
        var tooMany = PharmacyFailures.From(InvitationSenderErrors.TooManyToday(sender, tomorrow, refusedAt));

        recently.Code.ShouldBe("organisation.recipient-mailed-recently");
        recently.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        recently.Message.ShouldNotBeNullOrWhiteSpace();
        recently.Message.ShouldNotContain(address.Value);
        recently.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(mailAgainAt.Value);
        recently.ShouldBeOfType<RetryLaterFailure>().RefusedAt.ShouldBe(Option<DateTimeOffset>.Some(refusedAt.Value));
        tooMany.Code.ShouldBe("organisation.too-many-invitations-sent-today");
        tooMany.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        tooMany.Message.ShouldNotBeNullOrWhiteSpace();
        tooMany.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(tomorrow.Value);
        tooMany.ShouldBeOfType<RetryLaterFailure>().RefusedAt.ShouldBe(Option<DateTimeOffset>.Some(refusedAt.Value));
    }
}
