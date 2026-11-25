using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// The mail request's consumer issues a fresh link for an open invitation, commits its hash, then sends it; a failure
/// throws so it is retried, and an invitation that is no longer open or has run out is dropped.
/// </summary>
public partial class InvitationMailRequestedV1ConsumerTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Anna = EmailAddress.From("anna@example.org");

    private readonly InMemoryPharmacyStore store = new();
    private readonly IMailSender mails = Substitute.For<IMailSender>();
    private readonly FakeTimeProvider time = new(TestPharmacies.RegisteredAt.Value.AddMinutes(1));
    private readonly List<OutgoingMail> sent = [];
    private readonly List<int> commitsAtSend = [];

    public InvitationMailRequestedV1ConsumerTests()
    {
        mails.SendAsync(Arg.Do<OutgoingMail>(mail =>
        {
            commitsAtSend.Add(store.Commits);
            sent.Add(mail);
        }), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task An_invitation_sends_one_mail_to_the_address_with_a_link_that_carries_the_token_whose_hash_is_stored()
    {
        var (pharmacy, membership) = Invited();

        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        var mail = sent.ShouldHaveSingleItem();
        mail.To.ShouldBe("anna@example.org");
        mail.Subject.ShouldBe("Einladung zu Apotheke am Markt bei PharmaTraining");
        var token = TokenIn(mail);
        mail.Text.ShouldContain($"https://app.example/invitations/accept#token={token}", Case.Sensitive);
        mail.Text.ShouldContain("gültig bis 15.03.2027, 09:00 Uhr (UTC)", Case.Sensitive);
        pharmacy.Membership(membership).InvitationLinks.Select(link => link.Hash).ShouldBe([InvitationTokenHash.Of(InvitationToken.From(token))]);
        commitsAtSend.ShouldBe([1], "the hash was not committed before the mail was sent");
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_mail_that_cannot_be_sent_fails_the_consumer()
    {
        var (pharmacy, membership) = Invited();
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("The SMTP server is gone."));

        await Should.ThrowAsync<IOException>(
            () => Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken));

        store.Commits.ShouldBe(2);
        pharmacy.Membership(membership).InvitationLinks.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_lost_race_on_the_commit_is_thrown_so_it_is_retried_and_nothing_is_mailed()
    {
        var (pharmacy, membership) = Invited();
        store.FailNextCommit(new InvalidOperationException("lost race"));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken));

        thrown.Message.ShouldBe("lost race");
        await mails.DidNotReceive().SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invitation_revoked_after_the_links_commit_and_before_its_send_is_not_mailed()
    {
        var (pharmacy, membership) = Invited();
        store.OnCommit(() => RevokedByAnotherWriter(pharmacy, membership));

        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        store.Commits.ShouldBe(1);
        store.Stored.Single(stored => stored.Id == pharmacy.Id).Membership(membership).Status.ShouldBe(MembershipStatus.Revoked);
        sent.ShouldBeEmpty("the invitation was revoked after the link's commit and before the send, yet its invitee was mailed a dead link");
    }

    [Fact]
    public async Task An_invitation_renewed_after_the_links_commit_and_before_its_send_is_not_mailed_by_this_run()
    {
        var (pharmacy, membership) = Invited();
        store.OnCommit(() => RenewedByAnotherWriter(pharmacy, membership));

        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        store.Commits.ShouldBe(1);
        var stored = store.Stored.Single(stored => stored.Id == pharmacy.Id).Membership(membership);
        stored.Status.ShouldBe(MembershipStatus.Invited);
        stored.InvitationLinks.ShouldBeEmpty();
        sent.ShouldBeEmpty("the invitation was renewed after the link's commit and before the send, which killed the link, yet this run mailed it");
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("unknown membership")]
    [InlineData("unknown pharmacy")]
    public async Task An_invitation_that_is_no_longer_open_is_not_mailed(string which)
    {
        var (pharmacy, membership) = Invited();
        var message = which switch
        {
            "revoked" => Revoked(pharmacy, membership),
            "unknown membership" => new InvitationMailRequestedV1(Guid.CreateVersion7(), pharmacy.Id.Value),
            _ => new InvitationMailRequestedV1(membership.Value, Guid.CreateVersion7()),
        };

        await Consumer().HandleAsync(message, TestContext.Current.CancellationToken);

        await mails.DidNotReceive().SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_invitation_that_has_run_out_is_not_mailed()
    {
        var (pharmacy, membership) = Invited();
        time.Advance(TimeSpan.FromDays(Pharmacy.InvitationValidityDays));

        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        await mails.DidNotReceive().SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
        store.Commits.ShouldBe(0);
    }

    // #294: the expiry is stamped on the database's clock, so a link is issued on it too.
    [Fact]
    public async Task A_link_is_issued_and_mailed_on_the_database_clock_while_the_host_clock_is_past_the_expiry()
    {
        var (pharmacy, membership) = Invited();
        var expiresAt = TestPharmacies.RegisteredAt.Value.AddDays(Pharmacy.InvitationValidityDays);
        var host = new FakeTimeProvider(expiresAt.AddDays(1));
        var database = new FakeTimeProvider(expiresAt.AddMinutes(-1));

        await BothClocks.Consumer(store, mails, Links(), host, database: database)
            .HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        sent.ShouldHaveSingleItem("the link was issued on the host's clock, a day past the expiry, not on the database's, a minute before it");
        pharmacy.Membership(membership).InvitationLinks.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_invitation_run_out_on_the_database_clock_is_dropped_while_the_host_clock_is_before_its_expiry()
    {
        var (pharmacy, membership) = Invited();
        var expiresAt = TestPharmacies.RegisteredAt.Value.AddDays(Pharmacy.InvitationValidityDays);
        var host = new FakeTimeProvider(expiresAt.AddDays(-1));
        var database = new FakeTimeProvider(expiresAt.AddTicks(1));

        await BothClocks.Consumer(store, mails, Links(), host, database: database)
            .HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        sent.ShouldBeEmpty("the link was issued on the host's clock, a day before the expiry, not on the database's, a tick past it");
        pharmacy.Membership(membership).InvitationLinks.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Without_the_web_apps_address_nothing_is_stored_or_sent()
    {
        var (pharmacy, membership) = Invited();
        var unconfigured = new FrontendLinks(new ConfigurationBuilder().Build());

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => Consumer(unconfigured).HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain("Frontend:BaseUrl", Case.Sensitive);
        store.Commits.ShouldBe(0);
        await mails.DidNotReceive().SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(nameof(InvitationMailRequestedV1.MembershipId))]
    [InlineData(nameof(InvitationMailRequestedV1.PharmacyId))]
    public async Task A_message_with_an_empty_id_is_refused_naming_it(string field)
    {
        var (pharmacy, membership) = Invited();
        var message = field == nameof(InvitationMailRequestedV1.MembershipId)
            ? new InvitationMailRequestedV1(Guid.Empty, pharmacy.Id.Value)
            : new InvitationMailRequestedV1(membership.Value, Guid.Empty);

        var thrown = await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(message, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain(field, Case.Sensitive);
    }

    /// <summary>
    /// A mail whose send returned is not sent again for the same invitation, however often its message is delivered:
    /// Wolverine redelivers it when the process dies between the send and marking the envelope handled.
    /// </summary>
    [Fact]
    public async Task A_mail_whose_send_returned_is_not_sent_again_when_its_message_is_redelivered()
    {
        var (pharmacy, membership) = Invited();
        var message = new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value);

        await Consumer().HandleAsync(message, TestContext.Current.CancellationToken);
        await Consumer().HandleAsync(message, TestContext.Current.CancellationToken);

        var mail = sent.ShouldHaveSingleItem("the redelivered message mailed the same invitation a second time");
        LinkWorks(pharmacy, TokenIn(mail)).ShouldBeTrue("the mail already sent no longer opens the invitation after its message was redelivered");
    }

    /// <summary>
    /// A send that reached the server and failed afterwards is retried; when the retry cannot send at all (the server is
    /// gone now), the invitee holds only the first mail, and its link must still open the invitation.
    /// </summary>
    [Fact]
    public async Task A_retry_that_cannot_send_leaves_the_link_of_the_mail_that_reached_the_server_working()
    {
        var (pharmacy, membership) = Invited();
        List<OutgoingMail> received = [];
        var attempts = 0;
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (++attempts == 1)
            {
                // The server took the mail; the connection failed afterwards.
                received.Add(call.Arg<OutgoingMail>());
                return Task.FromException(new MailNotSentException("The mail was not sent: IOException."));
            }

            return Task.FromException(new IOException("The SMTP server is gone."));
        });
        var message = new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value);

        await Should.ThrowAsync<MailNotSentException>(() => Consumer().HandleAsync(message, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<IOException>(() => Consumer().HandleAsync(message, TestContext.Current.CancellationToken));

        var only = received.ShouldHaveSingleItem();
        LinkWorks(pharmacy, TokenIn(only)).ShouldBeTrue("the retry killed the only link the invitee holds, and mailed nothing in its place");
    }

    /// <summary>
    /// A send that reached the server and failed afterwards, then a retry that sent: the invitee holds two mails of one
    /// invitation, and either may arrive last, so the link of each must open it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Every_link_mailed_for_one_invitation_works_whichever_mail_arrives_last(int arrivesLast)
    {
        var (pharmacy, membership) = Invited();
        List<OutgoingMail> received = [];
        var attempts = 0;
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            received.Add(call.Arg<OutgoingMail>());
            return ++attempts == 1 ? Task.FromException(new MailNotSentException("The mail was not sent: IOException.")) : Task.CompletedTask;
        });
        var message = new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value);

        await Should.ThrowAsync<MailNotSentException>(() => Consumer().HandleAsync(message, TestContext.Current.CancellationToken));
        await Consumer().HandleAsync(message, TestContext.Current.CancellationToken);

        received.Count.ShouldBe(2);
        LinkWorks(pharmacy, TokenIn(received[arrivesLast])).ShouldBeTrue($"the link of mail {arrivesLast + 1} of 2, which the invitee may receive last, is dead");
    }

    /// <summary>A renewal is a new invitation version: its message mails a new link, and only that link works.</summary>
    [Fact]
    public async Task A_renewal_after_the_mail_was_sent_mails_a_new_link_that_replaces_the_first()
    {
        var (pharmacy, membership) = Invited();
        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromDays(1));
        pharmacy.RenewInvitation(membership, Timestamp.From(time.GetUtcNow()), InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        pharmacy.ClearDomainEvents();

        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        sent.Count.ShouldBe(2, "the renewal mailed no new link");
        var first = TokenIn(sent[0]);
        var renewed = TokenIn(sent[1]);
        renewed.ShouldNotBe(first);
        sent[1].Text.ShouldContain("gültig bis 16.03.2027, 09:01 Uhr (UTC)", Case.Sensitive);
        LinkWorks(pharmacy, first).ShouldBeFalse("the link mailed before the renewal still opens the invitation");
        LinkWorks(pharmacy, renewed).ShouldBeTrue("the renewal's link does not open the invitation");
    }

    /// <summary>
    /// Whether the link with this token opens the stored invitation: accepting it as anna@example.org succeeds. A
    /// successful accept changes the stored pharmacy, so a test asks this once per invitation it means to keep open.
    /// </summary>
    private bool LinkWorks(Pharmacy pharmacy, string token) =>
        store.Stored.Single(stored => stored.Id == pharmacy.Id)
            .AcceptInvitation(InvitationToken.From(token), PersonId.From(Guid.CreateVersion7()), DisplayName.From("Anna Berger"), Anna, Timestamp.From(time.GetUtcNow()))
            .IsSuccess;

    private static InvitationMailRequestedV1 Revoked(Pharmacy pharmacy, MembershipId membership)
    {
        pharmacy.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();
        return new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value);
    }

    /// <summary>
    /// What a manager's revoke leaves in the store once it commits between the consumer's commit and its send: the
    /// pharmacy as another writer loaded and saved it, a separate instance, so the consumer's own copy stays stale and
    /// only a read from the store sees the revoke.
    /// </summary>
    private void RevokedByAnotherWriter(Pharmacy pharmacy, MembershipId membership)
    {
        var founder = pharmacy.Membership(TestPharmacies.FounderOf(pharmacy));
        var revoked = Pharmacy.Register(
            pharmacy.Id,
            pharmacy.Name,
            founder.Id,
            Owner,
            DisplayName.From("Olaf Owner"),
            founder.Email!,
            JobFunction.Pharmacist,
            TestPharmacies.RegisteredAt);
        revoked.Invite(membership, Anna, JobFunction.Pta, MembershipRole.Employee, TestPharmacies.RegisteredAt, InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        revoked.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();
        revoked.ClearDomainEvents();
        store.Stored[store.Stored.IndexOf(pharmacy)] = revoked;
        pharmacy.Membership(membership).Status.ShouldBe(MembershipStatus.Invited, "the consumer's own copy must stay as it loaded it");
    }

    /// <summary>
    /// What a manager's renewal leaves in the store once it commits between the consumer's commit and its send: the
    /// membership still invited, its expiry moved on and its link cleared, so the link this run committed is dead. The
    /// renewal's own mail request mails the new link; this run's copy stays stale.
    /// </summary>
    private void RenewedByAnotherWriter(Pharmacy pharmacy, MembershipId membership)
    {
        var founder = pharmacy.Membership(TestPharmacies.FounderOf(pharmacy));
        var renewed = Pharmacy.Register(
            pharmacy.Id,
            pharmacy.Name,
            founder.Id,
            Owner,
            DisplayName.From("Olaf Owner"),
            founder.Email!,
            JobFunction.Pharmacist,
            TestPharmacies.RegisteredAt);
        renewed.Invite(membership, Anna, JobFunction.Pta, MembershipRole.Employee, TestPharmacies.RegisteredAt, InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        renewed.RenewInvitation(membership, Timestamp.From(time.GetUtcNow().AddMinutes(10)), InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        renewed.ClearDomainEvents();
        store.Stored[store.Stored.IndexOf(pharmacy)] = renewed;
        pharmacy.Membership(membership).InvitationLinks.ShouldNotBeEmpty("the consumer's own copy must keep the link it committed");
    }

    private static FrontendLinks Links() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Frontend:BaseUrl", "https://app.example")])
        .Build());

    private static string TokenIn(OutgoingMail mail)
    {
        var match = LinkToken().Match(mail.Text);
        match.Success.ShouldBeTrue($"the mail holds no invitation link: {mail.Text}");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("https://app\\.example/invitations/accept#token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex LinkToken();

    /// <summary>An open invitation of anna@example.org to "Apotheke am Markt", stored as its commit left it.</summary>
    private (Pharmacy Pharmacy, MembershipId Membership) Invited()
    {
        var pharmacy = TestPharmacies.Register(Owner, "Apotheke am Markt");
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        pharmacy.Stored(store);
        return (pharmacy, membership);
    }

    private InvitationMailRequestedV1Consumer Consumer(FrontendLinks? links = null) =>
        BothClocks.Consumer(store, mails, links ?? Links(), time);
}
