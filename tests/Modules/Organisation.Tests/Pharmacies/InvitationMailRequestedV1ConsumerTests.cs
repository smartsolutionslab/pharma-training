using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
        pharmacy.Membership(membership).TokenHash.ShouldBe(InvitationTokenHash.Of(InvitationToken.From(token)));
        commitsAtSend.ShouldBe([1], "the hash was not committed before the mail was sent");
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_mail_that_cannot_be_sent_fails_the_consumer()
    {
        var (pharmacy, membership) = Invited();
        var before = pharmacy.Membership(membership).TokenHash;
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("The SMTP server is gone."));

        await Should.ThrowAsync<IOException>(
            () => Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken));

        store.Commits.ShouldBe(1);
        pharmacy.Membership(membership).TokenHash.ShouldNotBe(before);
    }

    [Fact]
    public async Task A_retry_after_a_failed_send_mails_a_new_link_and_only_that_one_works()
    {
        var (pharmacy, membership) = Invited();
        List<OutgoingMail> attempted = [];
        var attempts = 0;
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            attempted.Add(call.Arg<OutgoingMail>());
            return ++attempts == 1 ? Task.FromException(new IOException("The SMTP server is gone.")) : Task.CompletedTask;
        });
        var message = new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value);

        await Should.ThrowAsync<IOException>(() => Consumer().HandleAsync(message, TestContext.Current.CancellationToken));
        await Consumer().HandleAsync(message, TestContext.Current.CancellationToken);

        attempted.Count.ShouldBe(2, "the retry sent no second mail");
        var first = TokenIn(attempted[0]);
        var second = TokenIn(attempted[1]);
        second.ShouldNotBe(first);
        pharmacy.Membership(membership).TokenHash.ShouldBe(InvitationTokenHash.Of(InvitationToken.From(second)));
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

    private static InvitationMailRequestedV1 Revoked(Pharmacy pharmacy, MembershipId membership)
    {
        pharmacy.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();
        return new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value);
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
        new(store, store, mails, links ?? Links(), time, NullLogger<InvitationMailRequestedV1Consumer>.Instance);
}
