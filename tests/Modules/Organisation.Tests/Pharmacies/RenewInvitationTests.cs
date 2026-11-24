using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager sends a fresh link: the old one stops working, and the invitation runs for another 14 days.</summary>
public class RenewInvitationTests
{
    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Invitee = EmailAddress.From("invitee@example.org");

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public async Task A_manager_renews_an_invitation_open_or_run_out_with_a_new_token_valid_for_14_days(int daysOld)
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var old = Token();
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.IssueInvitationLink(invitation, old, Timestamp.From(Start)).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromDays(daysOld) + TimeSpan.FromMinutes(10));
        var now = Start.AddDays(daysOld).AddMinutes(10);

        var renewed = await new RenewInvitationHandler(store, store, store, store, new FakeDatabaseClock(time), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, NullLogger<RenewInvitationHandler>.Instance)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        renewed.IsSuccess.ShouldBeTrue();
        var raised = pharmacy.DomainEvents.OfType<MemberInvited>().SingleOrDefault();
        raised.ShouldNotBeNull("renewing raised no MemberInvited");
        raised.Membership.ShouldBe(invitation);
        raised.ExpiresAt.ShouldBe(Timestamp.From(now.AddDays(14)));
        var membership = pharmacy.Membership(invitation);
        membership.Status.ShouldBe(MembershipStatus.Invited);
        membership.InvitationLinks.ShouldBeEmpty();
        membership.InvitationExpiresAt.ShouldBe(Timestamp.From(now.AddDays(14)));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_renewal_requests_its_mail_in_its_own_commit()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);

        var renewed = await Handler(store).HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        renewed.IsSuccess.ShouldBeTrue();
        var requested = store.Published.ShouldHaveSingleItem().ShouldBeOfType<InvitationMailRequestedV1>();
        requested.MembershipId.ShouldBe(invitation.Value);
        requested.PharmacyId.ShouldBe(pharmacy.Id.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_renewal_is_stored_without_a_link_until_its_mail_issues_one()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);

        var renewed = await Handler(store).HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        renewed.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(invitation).InvitationLinks.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_membership_that_is_no_open_invitation_of_this_pharmacy_is_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var revoked = TestPharmacies.Invite(pharmacy, "revoked@example.org");
        pharmacy.RevokeInvitation(revoked).IsSuccess.ShouldBeTrue();
        var ended = TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.EndMembership(ended, TestPharmacies.RegisteredAt.PlusDays(1)).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var elsewhere = TestPharmacies.Invite(TestPharmacies.Register(Owner, "Markt-Apotheke").Stored(store), "elsewhere@example.org");
        var handler = Handler(store);
        List<MembershipId> notOpenHere = [TestPharmacies.FounderOf(pharmacy), ended, revoked, MembershipId.New(), elsewhere];

        foreach (var membership in notOpenHere)
        {
            var refused = await handler.HandleAsync(new RenewInvitation(pharmacy.Id, membership, Owner), TestContext.Current.CancellationToken);

            refused.IsSuccess.ShouldBeFalse($"{membership} was renewed");
            refused.Error.Code.ShouldBe("organisation.no-open-invitation");
            refused.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        }

        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_employee_may_not_renew_and_a_stranger_is_told_the_pharmacy_does_not_exist()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);
        var before = pharmacy.Membership(invitation).InvitationExpiresAt;
        var handler = Handler(store);

        var employee = await handler.HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Anna), TestContext.Current.CancellationToken);
        var stranger = await handler.HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Ben), TestContext.Current.CancellationToken);

        employee.IsSuccess.ShouldBeFalse("an employee renewed an invitation");
        employee.Error.Code.ShouldBe("organisation.capability-missing");
        employee.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        stranger.IsSuccess.ShouldBeFalse("a stranger renewed an invitation");
        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        pharmacy.Membership(invitation).InvitationExpiresAt.ShouldBe(before);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_renewal_that_lost_the_race_is_told_the_pharmacy_changed()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = TestPharmacies.Invite(pharmacy, "invitee@example.org");
        pharmacy.Stored(store);
        store.LoseNextCommit();

        var lost = await Handler(store).HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        lost.IsSuccess.ShouldBeFalse("the lost commit was answered as a success");
        lost.Error.Code.ShouldBe("organisation.pharmacy-changed");
        lost.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_renewal_within_the_cooldown_answers_429_invitation_renewed_too_soon_and_commits_and_publishes_nothing()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        var expiresAt = pharmacy.Membership(invitation).InvitationExpiresAt;
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(4));

        var refused = await new RenewInvitationHandler(store, store, store, store, new FakeDatabaseClock(time), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, NullLogger<RenewInvitationHandler>.Instance)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("an invitation was renewed 4 minutes after its invite");
        refused.Error.Code.ShouldBe("organisation.invitation-renewed-too-soon");
        refused.Error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(Start.AddMinutes(10));
        pharmacy.Membership(invitation).InvitationExpiresAt.ShouldBe(expiresAt);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stored_mail_day_beyond_tomorrow_is_logged_as_a_warning_even_when_the_renewal_is_refused()
    {
        // The renewed invitation was requested a moment ago, so its own cooldown refuses the renewal; only the pharmacy's
        // mail day lies ahead, stored by a second invitation three days on (#294: a request time ahead warns on its own).
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Invite(MembershipId.New(), EmailAddress.From("second@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start.AddDays(3)), InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var logger = new FakeLogger<RenewInvitationHandler>();

        var refused = await new RenewInvitationHandler(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, logger)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("organisation.invitation-renewed-too-soon");
        var warning = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain(pharmacy.Id.Value.ToString());
        warning.Message.ShouldContain("2027-04-04");
        warning.Message.ShouldContain("2027-04-01");
        warning.Message.ShouldContain("the database's clock moved back");
    }

    // #294: a request time a cooldown or more ahead is not trusted, and that is logged like a mail day beyond tomorrow.
    [Fact]
    public async Task A_request_time_a_cooldown_or_more_ahead_is_logged_as_a_warning_naming_the_pharmacy_the_invitation_and_both_instants()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var requestedAt = Start.AddHours(1);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(requestedAt), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        var logger = new FakeLogger<RenewInvitationHandler>();

        var renewed = await new RenewInvitationHandler(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, logger)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        var warning = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning)
            .ShouldHaveSingleItem("a request time an hour ahead of the database's now logged no warning");
        warning.Message.ShouldContain(pharmacy.Id.Value.ToString());
        warning.Message.ShouldContain(invitation.Value.ToString());
        warning.Message.ShouldContain(requestedAt.ToString("O"));
        warning.Message.ShouldContain(Start.ToString("O"));
        warning.Message.ShouldContain("the database's clock moved back");
        warning.Message.ShouldNotContain(Invitee.Value, Case.Insensitive);
        renewed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_request_time_ahead_is_logged_even_when_another_limit_refuses_the_renewal()
    {
        var store = new InMemoryPharmacyStore();
        var capOfOne = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start.AddHours(1)), capOfOne).Value;
        pharmacy.Stored(store);
        var logger = new FakeLogger<RenewInvitationHandler>();

        var refused = await new RenewInvitationHandler(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), capOfOne, RecipientAndSenderMailLimits.Default, logger)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        var warning = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning)
            .ShouldHaveSingleItem("a request time ahead logged no warning when the pharmacy's daily cap refused the renewal");
        warning.Message.ShouldContain(invitation.Value.ToString());
        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.too-many-invitations-today");
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_request_time_within_the_cooldown_logs_no_warning()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start.AddMinutes(10).AddSeconds(-1)), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        var logger = new FakeLogger<RenewInvitationHandler>();

        var refused = await new RenewInvitationHandler(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, logger)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level == LogLevel.Warning);
        refused.Error.Code.ShouldBe("organisation.invitation-renewed-too-soon");
    }

    [Fact]
    public async Task The_renewal_cooldown_runs_on_the_database_clock_not_the_host_clock()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        await using var services = new ServiceCollection()
            .AddSingleton<IPharmacyStore>(store)
            .AddSingleton<IInvitationRecipientStore>(store)
            .AddSingleton<IInvitationSenderStore>(store)
            .AddSingleton<IOrganisationUnitOfWork>(store)
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Start.AddDays(3)))
            .AddSingleton<IDatabaseClock>(new FakeDatabaseClock(new FakeTimeProvider(Start.AddMinutes(4))))
            .AddSingleton(InvitationMailLimits.Default)
            .AddSingleton(RecipientAndSenderMailLimits.Default)
            .AddLogging()
            .BuildServiceProvider();

        var refused = await ActivatorUtilities.CreateInstance<RenewInvitationHandler>(services)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("the cooldown ran on the host's clock, three days ahead of the database's");
        refused.Error.Code.ShouldBe("organisation.invitation-renewed-too-soon");
        refused.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(Start.AddMinutes(10));
        store.Commits.ShouldBe(0);
    }

    // #265: a renewal counts on its address and on the manager who renews, like an invite.
    [Fact]
    public async Task A_renewal_counts_on_the_renewing_manager_not_on_who_invited()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Manager);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);

        var renewed = await Handler(store, new FakeTimeProvider(Start.AddMinutes(10)))
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Anna), TestContext.Current.CancellationToken);

        renewed.IsSuccess.ShouldBeTrue();
        var sender = store.StoredSenders.ShouldHaveSingleItem("the renewal stored no sender");
        sender.Id.ShouldBe(Anna, "the renewal counted on who invited, not on who renewed");
        sender.MailsThatDay.Value.ShouldBe(1);
        var recipient = store.StoredRecipients.ShouldHaveSingleItem("the renewal stored no recipient");
        recipient.Id.ShouldBe(InvitationRecipientId.For(Invitee));
        recipient.LastMailedAt.ShouldBe(Timestamp.From(Start.AddMinutes(10)));
    }

    [Fact]
    public async Task A_renewal_of_an_address_another_pharmacy_mailed_within_the_cooldown_answers_429_recipient_mailed_recently()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        store.StoredRecipients.Add(InvitationRecipient.FirstMailed(Invitee, Timestamp.From(Start.AddMinutes(8))));

        var refused = await Handler(store, new FakeTimeProvider(Start.AddMinutes(10)))
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("an address another pharmacy mailed 2 minutes ago was mailed again by a renewal");
        refused.Error.Code.ShouldBe("organisation.recipient-mailed-recently");
        refused.Error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(Start.AddMinutes(18));
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
        store.StoredSenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_renewal_by_a_sender_at_the_daily_cap_answers_429_too_many_invitations_sent_today()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        store.StoredSenders.Add(InvitationSender.FirstMailed(Owner, Timestamp.From(Start)));
        var capOfOne = new RecipientAndSenderMailLimits(RecipientCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));

        var refused = await Handler(store, new FakeTimeProvider(Start.AddMinutes(10)), capOfOne)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("a sender capped at one mail a day renewed a second");
        refused.Error.Code.ShouldBe("organisation.too-many-invitations-sent-today");
        refused.Error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(new DateTimeOffset(2027, 4, 2, 0, 0, 0, TimeSpan.Zero));
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
        store.StoredSenders.ShouldHaveSingleItem().MailsThatDay.Value.ShouldBe(1);
    }

    [Fact]
    public async Task A_renewal_within_its_own_cooldown_is_still_invitation_renewed_too_soon()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), Invitee, JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        store.StoredRecipients.Add(InvitationRecipient.FirstMailed(Invitee, Timestamp.From(Start)));
        var capOfOne = new RecipientAndSenderMailLimits(RecipientCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        store.StoredSenders.Add(InvitationSender.FirstMailed(Owner, Timestamp.From(Start)));

        var refused = await Handler(store, new FakeTimeProvider(Start.AddMinutes(4)), capOfOne)
            .HandleAsync(new RenewInvitation(pharmacy.Id, invitation, Owner), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.invitation-renewed-too-soon");
    }

    private static RenewInvitationHandler Handler(InMemoryPharmacyStore store, FakeTimeProvider time, RecipientAndSenderMailLimits? recipientAndSender = null) =>
        new(store, store, store, store, new FakeDatabaseClock(time), InvitationMailLimits.Default, recipientAndSender ?? RecipientAndSenderMailLimits.Default, NullLogger<RenewInvitationHandler>.Instance);

    private static RenewInvitationHandler Handler(InMemoryPharmacyStore store) => new(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, NullLogger<RenewInvitationHandler>.Instance);

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));
}
