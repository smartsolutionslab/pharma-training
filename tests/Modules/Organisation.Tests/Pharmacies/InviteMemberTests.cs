using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A manager invites an address; the invitation keeps only its token's hash and is refused without a manager's say.</summary>
public class InviteMemberTests
{
    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Ben = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Invitee = EmailAddress.From("invitee@example.org");

    [Fact]
    public async Task A_manager_invites_an_address_and_the_invitation_is_committed()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(5));

        var invited = await new InviteMemberHandler(store, store, store, store, new FakeDatabaseClock(time), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, NullLogger<InviteMemberHandler>.Instance)
            .HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pka, MembershipRole.Manager), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        var membership = pharmacy.Memberships.SingleOrDefault(candidate => candidate.Id.Value == invited.Value);
        membership.ShouldNotBeNull($"no membership {invited.Value} was stored");
        membership.Status.ShouldBe(MembershipStatus.Invited);
        membership.Email.ShouldBe(Invitee);
        membership.JobFunction.ShouldBe(JobFunction.Pka);
        membership.Role.ShouldBe(MembershipRole.Manager);
        membership.InvitationExpiresAt.ShouldBe(Timestamp.From(Start.AddMinutes(5).AddDays(14)));
        var raised = pharmacy.DomainEvents.OfType<MemberInvited>().ShouldHaveSingleItem();
        raised.Membership.ShouldBe(membership.Id);
        raised.ExpiresAt.ShouldBe(Timestamp.From(Start.AddMinutes(5).AddDays(14)));
        membership.InvitationLinks.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_invitation_requests_its_mail_in_its_own_commit()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);

        var invited = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        var requested = store.Published.ShouldHaveSingleItem().ShouldBeOfType<InvitationMailRequestedV1>();
        requested.MembershipId.ShouldBe(invited.Value);
        requested.PharmacyId.ShouldBe(pharmacy.Id.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_request_that_goes_away_as_its_commit_completes_still_has_its_mail_requested()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        using var request = new CancellationTokenSource();
        store.OnCommit(request.Cancel);

        var invited = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), request.Token);

        request.IsCancellationRequested.ShouldBeTrue();
        invited.IsSuccess.ShouldBeTrue();
        store.Published.ShouldHaveSingleItem().ShouldBeOfType<InvitationMailRequestedV1>().MembershipId.ShouldBe(invited.Value);
    }

    [Fact]
    public async Task An_invitation_is_stored_without_a_link_until_its_mail_issues_one()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);

        var invited = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        pharmacy.Membership(MembershipId.From(invited.Value)).InvitationLinks.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_employee_may_not_invite()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.AddMember(pharmacy, Anna, MembershipRole.Employee);
        pharmacy.Stored(store);

        var refused = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Anna, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("an employee's invitation was accepted");
        refused.Error.Code.ShouldBe("organisation.capability-missing");
        refused.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        pharmacy.Memberships.ShouldNotContain(membership => membership.Email == Invitee);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_stranger_and_an_unknown_pharmacy_are_not_found()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var handler = Handler(store);

        var stranger = await handler.HandleAsync(new InviteMember(pharmacy.Id, Ben, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);
        var unknown = await handler.HandleAsync(new InviteMember(PharmacyId.New(), Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        stranger.IsSuccess.ShouldBeFalse("a stranger's invitation was accepted");
        stranger.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        stranger.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.IsSuccess.ShouldBeFalse("an invitation to an unknown pharmacy was accepted");
        unknown.Error.Code.ShouldBe("organisation.pharmacy-not-found");
        unknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        pharmacy.Memberships.ShouldNotContain(membership => membership.Email == Invitee);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_address_that_is_invited_or_active_is_refused_as_a_conflict()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        TestPharmacies.Invite(pharmacy, "invited@example.org");
        pharmacy.Stored(store);
        var founder = pharmacy.Membership(TestPharmacies.FounderOf(pharmacy)).Email;
        var handler = Handler(store);
        List<EmailAddress> taken = [EmailAddress.From("Invited@Example.org"), founder];

        foreach (var address in taken)
        {
            var refused = await handler.HandleAsync(new InviteMember(pharmacy.Id, Owner, address, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

            refused.IsSuccess.ShouldBeFalse($"{address} was invited again");
            refused.Error.Code.ShouldBe("organisation.already-member-or-invited");
            refused.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        }

        pharmacy.Memberships.Count.ShouldBe(2);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_invitation_that_lost_the_race_is_told_the_pharmacy_changed()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        store.LoseNextCommit();

        var lost = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        lost.IsSuccess.ShouldBeFalse("the lost commit was answered as a success");
        lost.Error.Code.ShouldBe("organisation.pharmacy-changed");
        lost.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invite_at_the_daily_cap_answers_429_too_many_invitations_today_and_commits_and_publishes_nothing()
    {
        var store = new InMemoryPharmacyStore();
        var capOfOne = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        var pharmacy = TestPharmacies.Register(Owner);
        pharmacy.Invite(MembershipId.New(), EmailAddress.From("first@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), capOfOne)
            .IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var memberships = pharmacy.Memberships.Count;
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromHours(1));

        var refused = await new InviteMemberHandler(store, store, store, store, new FakeDatabaseClock(time), capOfOne, RecipientAndSenderMailLimits.Default, NullLogger<InviteMemberHandler>.Instance)
            .HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("a second invitation mail was requested on a day capped at one");
        refused.Error.Code.ShouldBe("organisation.too-many-invitations-today");
        refused.Error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(new DateTimeOffset(2027, 4, 2, 0, 0, 0, TimeSpan.Zero));
        pharmacy.Memberships.Count.ShouldBe(memberships);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stored_mail_day_beyond_tomorrow_is_logged_as_a_warning_naming_the_pharmacy_and_both_days()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        pharmacy.Invite(MembershipId.New(), EmailAddress.From("ahead@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start.AddDays(3)), InvitationMailLimits.Default)
            .IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var logger = new FakeLogger<InviteMemberHandler>();

        var invited = await new InviteMemberHandler(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, logger)
            .HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        var warning = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain(pharmacy.Id.Value.ToString());
        warning.Message.ShouldContain("2027-04-04");
        warning.Message.ShouldContain("2027-04-01");
        warning.Message.ShouldContain("the database's clock moved back");
    }

    [Fact]
    public async Task A_stored_mail_day_of_tomorrow_is_no_clock_fault_and_logs_no_warning()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        pharmacy.Invite(MembershipId.New(), EmailAddress.From("ahead@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start.AddDays(1)), InvitationMailLimits.Default)
            .IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var logger = new FakeLogger<InviteMemberHandler>();

        var invited = await new InviteMemberHandler(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, logger)
            .HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invitation_is_counted_and_dated_on_the_database_clock_not_the_host_clock()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        await using var services = new ServiceCollection()
            .AddSingleton<IPharmacyStore>(store)
            .AddSingleton<IInvitationRecipientStore>(store)
            .AddSingleton<IInvitationSenderStore>(store)
            .AddSingleton<IOrganisationUnitOfWork>(store)
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Start.AddDays(3)))
            .AddSingleton<IDatabaseClock>(new FakeDatabaseClock(new FakeTimeProvider(Start)))
            .AddSingleton(InvitationMailLimits.Default)
            .AddSingleton(RecipientAndSenderMailLimits.Default)
            .AddLogging()
            .BuildServiceProvider();

        var invited = await ActivatorUtilities.CreateInstance<InviteMemberHandler>(services)
            .HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        pharmacy.InvitationMailDay.ShouldBe(UtcDay.Of(Timestamp.From(Start)), "the mail was counted on the host's day, three days ahead of the database's");
        var membership = pharmacy.Memberships.Single(candidate => candidate.Id.Value == invited.Value);
        membership.InvitationRequestedAt.ShouldBe(Timestamp.From(Start));
        membership.InvitationExpiresAt.ShouldBe(Timestamp.From(Start.AddDays(14)));
    }

    // #265: each invite counts on its address (one mail per address per cooldown, across pharmacies) and on its
    // sender (so many a UTC day, across pharmacies). A refusal commits and publishes nothing.
    [Fact]
    public async Task An_invite_counts_the_address_and_the_sender_in_its_commit()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);

        var invited = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, EmailAddress.From(" Invitee@Example.org "), JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        store.Commits.ShouldBe(1);
        var recipient = store.StoredRecipients.ShouldHaveSingleItem("the invite stored no recipient");
        recipient.Id.ShouldBe(InvitationRecipientId.For(Invitee));
        recipient.LastMailedAt.ShouldBe(Timestamp.From(Start));
        var sender = store.StoredSenders.ShouldHaveSingleItem("the invite stored no sender");
        sender.Id.ShouldBe(Owner);
        sender.MailDay.ShouldBe(UtcDay.Of(Timestamp.From(Start)));
        sender.MailsThatDay.Value.ShouldBe(1);
    }

    [Fact]
    public async Task An_invite_of_an_address_mailed_within_the_cooldown_from_another_pharmacy_answers_429_recipient_mailed_recently_and_commits_and_publishes_nothing()
    {
        var store = new InMemoryPharmacyStore();
        var other = TestPharmacies.Register(Ben, "Rosen-Apotheke").Stored(store);
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var time = new FakeTimeProvider(Start);
        (await Handler(store, time).HandleAsync(new InviteMember(other.Id, Ben, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        time.Advance(TimeSpan.FromMinutes(4));

        var refused = await Handler(store, time).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("an address another pharmacy mailed 4 minutes ago was mailed again");
        refused.Error.Code.ShouldBe("organisation.recipient-mailed-recently");
        refused.Error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(Start.AddMinutes(10));
        refused.Error.Message.ShouldNotContain(other.Id.Value.ToString());
        store.Commits.ShouldBe(1);
        store.Published.ShouldHaveSingleItem();
        store.StoredRecipients.ShouldHaveSingleItem().LastMailedAt.ShouldBe(Timestamp.From(Start));
        store.StoredSenders.ShouldNotContain(sender => sender.Id == Owner, "the refused invite counted on its sender");
    }

    [Fact]
    public async Task A_re_invite_after_a_revoke_within_the_cooldown_answers_429_recipient_mailed_recently()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var time = new FakeTimeProvider(Start);
        var first = await Handler(store, time).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();
        pharmacy.RevokeInvitation(MembershipId.From(first.Value)).IsSuccess.ShouldBeTrue();
        pharmacy.ClearDomainEvents();
        time.Advance(TimeSpan.FromMinutes(1));

        var refused = await Handler(store, time).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("a revoke and re-invite mailed the address again a minute later");
        refused.Error.Code.ShouldBe("organisation.recipient-mailed-recently");
        refused.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(Start.AddMinutes(10));
        store.Commits.ShouldBe(1);
        store.Published.ShouldHaveSingleItem();
        store.StoredRecipients.ShouldHaveSingleItem().LastMailedAt.ShouldBe(Timestamp.From(Start), "a revoke gave the address's cooldown back");
    }

    [Fact]
    public async Task An_invite_by_a_sender_at_the_daily_cap_answers_429_too_many_invitations_sent_today_and_commits_and_publishes_nothing()
    {
        var store = new InMemoryPharmacyStore();
        var first = TestPharmacies.Register(Owner).Stored(store);
        var second = TestPharmacies.Register(Owner, "Rosen-Apotheke").Stored(store);
        var time = new FakeTimeProvider(Start);
        var capOfOne = new RecipientAndSenderMailLimits(RecipientCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        (await Handler(store, time, capOfOne).HandleAsync(new InviteMember(first.Id, Owner, EmailAddress.From("anna@example.org"), JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        time.Advance(TimeSpan.FromHours(1));

        var refused = await Handler(store, time, capOfOne).HandleAsync(new InviteMember(second.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse("a sender capped at one mail a day sent a second from another pharmacy");
        refused.Error.Code.ShouldBe("organisation.too-many-invitations-sent-today");
        refused.Error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(new DateTimeOffset(2027, 4, 2, 0, 0, 0, TimeSpan.Zero));
        store.Commits.ShouldBe(1);
        store.Published.ShouldHaveSingleItem();
        store.StoredRecipients.ShouldNotContain(recipient => recipient.Id == InvitationRecipientId.For(Invitee), "the refused invite counted on its address");
        store.StoredSenders.ShouldHaveSingleItem().MailsThatDay.Value.ShouldBe(1);
    }

    [Fact]
    public async Task Another_sender_and_another_address_are_not_limited_by_them()
    {
        var store = new InMemoryPharmacyStore();
        var owners = TestPharmacies.Register(Owner).Stored(store);
        var annas = TestPharmacies.Register(Anna, "Rosen-Apotheke").Stored(store);
        var time = new FakeTimeProvider(Start);
        var capOfTwo = new RecipientAndSenderMailLimits(RecipientCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(2));
        var handler = Handler(store, time, capOfTwo);

        var invitee = await handler.HandleAsync(new InviteMember(owners.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);
        var anotherAddress = await handler.HandleAsync(new InviteMember(owners.Id, Owner, EmailAddress.From("ben@example.org"), JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);
        var anotherSender = await handler.HandleAsync(new InviteMember(annas.Id, Anna, EmailAddress.From("cleo@example.org"), JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invitee.IsSuccess.ShouldBeTrue();
        anotherAddress.IsSuccess.ShouldBeTrue("an address was refused because another address was mailed a moment ago");
        anotherSender.IsSuccess.ShouldBeTrue("a sender was refused because another sender reached the daily cap");
        store.StoredSenders.Where(sender => sender.Id == Owner).ShouldHaveSingleItem("the owner's mails were not counted").MailsThatDay.Value.ShouldBe(2);
        store.StoredSenders.Where(sender => sender.Id == Anna).ShouldHaveSingleItem("Anna's mail was not counted").MailsThatDay.Value.ShouldBe(1);
        store.StoredRecipients.Count.ShouldBe(3);
    }

    [Fact]
    public async Task An_address_already_invited_is_still_a_conflict_within_the_recipient_cooldown()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        var time = new FakeTimeProvider(Start);
        (await Handler(store, time).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        time.Advance(TimeSpan.FromMinutes(1));

        var refused = await Handler(store, time).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        refused.IsSuccess.ShouldBeFalse();
        refused.Error.Code.ShouldBe("organisation.already-member-or-invited");
        refused.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_pharmacy_cap_is_answered_before_the_recipient_cooldown_and_the_recipient_cooldown_before_the_sender_cap()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacyCapOfOne = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        var senderCapOfOne = new RecipientAndSenderMailLimits(RecipientCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        var full = TestPharmacies.Register(Owner);
        full.Invite(MembershipId.New(), EmailAddress.From("first@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), pharmacyCapOfOne)
            .IsSuccess.ShouldBeTrue();
        full.Stored(store);
        var open = TestPharmacies.Register(Owner, "Rosen-Apotheke").Stored(store);
        var another = TestPharmacies.Register(Owner, "Linden-Apotheke").Stored(store);
        store.StoredRecipients.Add(InvitationRecipient.FirstMailed(Invitee, Timestamp.From(Start)));
        store.StoredSenders.Add(InvitationSender.FirstMailed(Owner, Timestamp.From(Start)));
        var time = new FakeTimeProvider(Start.AddMinutes(1));
        var handler = new InviteMemberHandler(store, store, store, store, new FakeDatabaseClock(time), pharmacyCapOfOne, senderCapOfOne, NullLogger<InviteMemberHandler>.Instance);

        var pharmacyCap = await handler.HandleAsync(new InviteMember(full.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);
        var recipientCooldown = await handler.HandleAsync(new InviteMember(open.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);
        var senderCap = await handler.HandleAsync(new InviteMember(another.Id, Owner, EmailAddress.From("ben@example.org"), JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        pharmacyCap.IsSuccess.ShouldBeFalse();
        pharmacyCap.Error.Code.ShouldBe("organisation.too-many-invitations-today");
        recipientCooldown.IsSuccess.ShouldBeFalse();
        recipientCooldown.Error.Code.ShouldBe("organisation.recipient-mailed-recently");
        senderCap.IsSuccess.ShouldBeFalse();
        senderCap.Error.Code.ShouldBe("organisation.too-many-invitations-sent-today");
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invitation_that_lost_the_race_stores_neither_its_address_nor_its_sender()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        store.LoseNextCommit();

        var lost = await Handler(store).HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);
        await store.CommitAsync(TestContext.Current.CancellationToken);

        lost.IsSuccess.ShouldBeFalse();
        lost.Error.Code.ShouldBe("organisation.pharmacy-changed");
        store.StoredRecipients.ShouldBeEmpty();
        store.StoredSenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stored_sender_mail_day_beyond_tomorrow_is_logged_as_a_warning_and_today_starts_the_tally_afresh()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        store.StoredSenders.Add(InvitationSender.FirstMailed(Owner, Timestamp.From(Start.AddDays(3))));
        var logger = new FakeLogger<InviteMemberHandler>();

        var invited = await new InviteMemberHandler(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, logger)
            .HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        var warning = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ShouldHaveSingleItem("a sender's mail day three days ahead was not logged");
        warning.Message.ShouldContain(Owner.Value.ToString());
        warning.Message.ShouldContain("2027-04-04");
        warning.Message.ShouldContain("2027-04-01");
        warning.Message.ShouldNotContain(Invitee.Value);
        warning.Message.ShouldContain("the database's clock moved back");
        var sender = store.StoredSenders.ShouldHaveSingleItem();
        sender.MailDay.ShouldBe(UtcDay.Of(Timestamp.From(Start)));
        sender.MailsThatDay.Value.ShouldBe(1);
    }

    [Fact]
    public async Task The_address_and_the_sender_are_counted_on_the_database_clock_not_the_host_clock()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        await using var services = new ServiceCollection()
            .AddSingleton<IPharmacyStore>(store)
            .AddSingleton<IInvitationRecipientStore>(store)
            .AddSingleton<IInvitationSenderStore>(store)
            .AddSingleton<IOrganisationUnitOfWork>(store)
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Start.AddDays(3)))
            .AddSingleton<IDatabaseClock>(new FakeDatabaseClock(new FakeTimeProvider(Start)))
            .AddSingleton(InvitationMailLimits.Default)
            .AddSingleton(RecipientAndSenderMailLimits.Default)
            .AddLogging()
            .BuildServiceProvider();

        var invited = await ActivatorUtilities.CreateInstance<InviteMemberHandler>(services)
            .HandleAsync(new InviteMember(pharmacy.Id, Owner, Invitee, JobFunction.Pta, MembershipRole.Employee), TestContext.Current.CancellationToken);

        invited.IsSuccess.ShouldBeTrue();
        store.StoredRecipients.ShouldHaveSingleItem().LastMailedAt.ShouldBe(Timestamp.From(Start), "the address was counted on the host's clock");
        store.StoredSenders.ShouldHaveSingleItem().MailDay.ShouldBe(UtcDay.Of(Timestamp.From(Start)), "the sender was counted on the host's day");
    }

    [Fact]
    public void By_default_an_address_is_mailed_at_most_every_10_minutes_and_a_sender_sends_at_most_100_a_day()
    {
        RecipientAndSenderMailLimits.Default.RecipientCooldown.Value.ShouldBe(TimeSpan.FromMinutes(10));
        RecipientAndSenderMailLimits.Default.MailsPerSenderPerDay.Value.ShouldBe(100);
    }

    private static InviteMemberHandler Handler(InMemoryPharmacyStore store, FakeTimeProvider time, RecipientAndSenderMailLimits? recipientAndSender = null) =>
        new(store, store, store, store, new FakeDatabaseClock(time), InvitationMailLimits.Default, recipientAndSender ?? RecipientAndSenderMailLimits.Default, NullLogger<InviteMemberHandler>.Instance);

    private static InviteMemberHandler Handler(InMemoryPharmacyStore store) => new(store, store, store, store, new FakeDatabaseClock(new FakeTimeProvider(Start)), InvitationMailLimits.Default, RecipientAndSenderMailLimits.Default, NullLogger<InviteMemberHandler>.Instance);
}
