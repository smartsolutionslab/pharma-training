using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// A send that failed before the server could have taken the mail leaves no link behind: the consumer withdraws the
/// link it issued, commits that, and still throws, so Wolverine retries it and finally dead-letters it. Only links
/// whose mails may be out count towards the cap, so an SMTP outage never reaches it.
/// </summary>
public class InvitationLinkWithdrawalTests
{
    /// <summary>Wolverine's first try and its six retries (1, 2, 4 … 32 s) before it dead-letters a message.</summary>
    private const int AttemptsBeforeDeadLetter = 7;

    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Anna = EmailAddress.From("anna@example.org");

    private readonly InMemoryPharmacyStore store = new();
    private readonly IMailSender mails = Substitute.For<IMailSender>();
    private readonly FakeTimeProvider time = new(TestPharmacies.RegisteredAt.Value.AddMinutes(1));
    private readonly FakeLogger<InvitationMailRequestedV1Consumer> logger = new();

    [Fact]
    public async Task A_send_refused_before_the_server_took_the_mail_leaves_the_link_set_as_it_was_and_still_throws()
    {
        var (pharmacy, membership) = Invited();
        var refused = new MailNotSentException("The mail was not sent: IOException.", mayHaveBeenDelivered: false);
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(refused));

        var thrown = await Should.ThrowAsync<MailNotSentException>(() => Consumer().HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(refused);
        Stored(pharmacy, membership).InvitationLinks.ShouldBeEmpty("a link whose mail never reached the server was left behind");
    }

    [Fact]
    public async Task A_bound_that_fired_before_the_send_started_leaves_the_link_set_as_it_was_and_still_throws()
    {
        var (pharmacy, membership) = Invited();
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new MailSendTimedOutException(mayHaveBeenDelivered: false, new CancellationToken(canceled: true))));

        await Should.ThrowAsync<OperationCanceledException>(() => Consumer().HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken));

        Stored(pharmacy, membership).InvitationLinks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failures_before_the_server_took_the_mail_never_reach_the_cap_so_the_message_fails_on_every_attempt_and_dead_letters()
    {
        var (pharmacy, membership) = Invited();
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new MailNotSentException("The mail was not sent: SocketException.", mayHaveBeenDelivered: false)));

        for (var attempt = 1; attempt <= AttemptsBeforeDeadLetter + Pharmacy.MaxInvitationLinksPerVersion; attempt++)
        {
            await Should.ThrowAsync<MailNotSentException>(
                () => Consumer().HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken),
                $"attempt {attempt} did not fail, so the message would not be dead-lettered");
        }

        await mails.Received(AttemptsBeforeDeadLetter + Pharmacy.MaxInvitationLinksPerVersion).SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
        Stored(pharmacy, membership).InvitationLinks.ShouldBeEmpty();
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Message.Contains("invitation-link-limit-reached", StringComparison.Ordinal));
    }

    /// <summary>
    /// The server refusing MAIL FROM, RCPT TO or DATA does not hold the mail, although the send had started, so the real
    /// sender says so and every attempt withdraws its link: the cap is never reached, every attempt throws, and the
    /// message dead-letters (review B1, rules a and c).
    /// </summary>
    [Theory]
    [InlineData("MAIL")]
    [InlineData("RCPT")]
    [InlineData("DATA")]
    public async Task Server_refusals_never_reach_the_cap_so_every_attempt_throws(string refused)
    {
        var (pharmacy, membership) = Invited();
        await using var server = new RefusingSmtpServer(refused);
        var sender = new SmtpMailSender(
            new ConfigurationBuilder()
                .AddInMemoryCollection([
                    new KeyValuePair<string, string?>("ConnectionStrings:mail", server.ConnectionString),
                    new KeyValuePair<string, string?>("Mail:From", "noreply@example.com"),
                ])
                .Build(),
            time);
        var consumer = new InvitationMailRequestedV1Consumer(store, store, sender, Links(), time, logger);

        for (var attempt = 1; attempt <= AttemptsBeforeDeadLetter + Pharmacy.MaxInvitationLinksPerVersion; attempt++)
        {
            await Should.ThrowAsync<MailNotSentException>(
                () => consumer.HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken),
                $"attempt {attempt} against a server refusing {refused} did not fail, so the message would not be dead-lettered");
        }

        server.Taken.ShouldBe(0);
        Stored(pharmacy, membership).InvitationLinks.ShouldBeEmpty($"a link whose mail the server refused at {refused} was left behind");
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Message.Contains("invitation-link-limit-reached", StringComparison.Ordinal));
    }

    /// <summary>
    /// Any failure of the send other than one that says the mail may be out takes the link back, and still throws
    /// (review B1, rule c).
    /// </summary>
    [Fact]
    public async Task An_invalid_operation_from_the_sender_withdraws_the_link_and_still_throws()
    {
        var (pharmacy, membership) = Invited();
        var failure = new InvalidOperationException("The sender address is missing or unusable: set Mail:From to an e-mail address.");
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => Consumer().HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(failure);
        Stored(pharmacy, membership).InvitationLinks.ShouldBeEmpty("a link whose mail failed with a plain exception was left behind");
    }

    [Fact]
    public async Task A_send_that_may_have_reached_the_server_keeps_its_link_and_still_throws()
    {
        var (pharmacy, membership) = Invited();
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new MailNotSentException("The mail was not sent: IOException.", mayHaveBeenDelivered: true)));

        await Should.ThrowAsync<MailNotSentException>(() => Consumer().HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken));

        Stored(pharmacy, membership).InvitationLinks.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_withdrawal_whose_commit_fails_is_logged_and_the_send_failure_is_still_thrown()
    {
        var (pharmacy, membership) = Invited();
        var refused = new MailNotSentException("The mail was not sent: IOException.", mayHaveBeenDelivered: false);
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(refused));
        store.LoseNextCommit();

        var thrown = await Should.ThrowAsync<MailNotSentException>(() => Consumer().HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(refused);
        var warning = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain(membership.Value.ToString(), Case.Sensitive);
    }

    [Fact]
    public void Withdrawing_a_link_takes_back_only_that_one_and_raises_invitation_link_withdrawn()
    {
        var (pharmacy, membership) = Invited();
        var kept = Token();
        var withdrawn = Token();
        pharmacy.IssueInvitationLink(membership, kept, Now()).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(membership, withdrawn, Now()).IsSuccess.ShouldBeTrue();
        pharmacy.ClearDomainEvents();

        pharmacy.WithdrawInvitationLink(membership, withdrawn).IsSuccess.ShouldBeTrue();

        pharmacy.Membership(membership).InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(kept)]);
        var raised = pharmacy.DomainEvents.OfType<InvitationLinkWithdrawn>().ShouldHaveSingleItem();
        raised.Pharmacy.ShouldBe(pharmacy.Id);
        raised.Membership.ShouldBe(membership);
    }

    [Fact]
    public void Withdrawing_a_link_the_version_no_longer_holds_changes_nothing()
    {
        var (pharmacy, membership) = Invited();
        var kept = Token();
        pharmacy.IssueInvitationLink(membership, kept, Now()).IsSuccess.ShouldBeTrue();
        pharmacy.ClearDomainEvents();

        pharmacy.WithdrawInvitationLink(membership, Token()).IsSuccess.ShouldBeTrue();

        pharmacy.Membership(membership).InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(kept)]);
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("unknown")]
    public void A_link_is_withdrawn_only_from_an_open_invitation(string which)
    {
        var (pharmacy, membership) = Invited();
        var target = membership;
        if (which == "revoked")
        {
            pharmacy.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();
        }
        else
        {
            target = MembershipId.New();
        }

        pharmacy.ClearDomainEvents();

        pharmacy.WithdrawInvitationLink(target, Token()).Error.ShouldBeOfType<NoOpenInvitation>();
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));

    private static InvitationMailRequestedV1 Request(Pharmacy pharmacy, MembershipId membership) => new(membership.Value, pharmacy.Id.Value);

    private static FrontendLinks Links() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Frontend:BaseUrl", "https://app.example")])
        .Build());

    private Timestamp Now() => Timestamp.From(time.GetUtcNow());

    private (Pharmacy Pharmacy, MembershipId Membership) Invited()
    {
        var pharmacy = TestPharmacies.Register(Owner, "Apotheke am Markt");
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        pharmacy.Stored(store);
        return (pharmacy, membership);
    }

    private Membership Stored(Pharmacy pharmacy, MembershipId membership) =>
        store.Stored.Single(stored => stored.Id == pharmacy.Id).Membership(membership);

    private InvitationMailRequestedV1Consumer Consumer() => new(store, store, mails, Links(), time, logger);
}
