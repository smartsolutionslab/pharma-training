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
/// The invitation's version and the mark that its mail was sent: the mark is recorded only for the open version, and
/// once the mail is out, a mark that cannot be stored is logged, never thrown, since a throw would mail it again.
/// </summary>
public class InvitationMailedTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Anna = EmailAddress.From("anna@example.org");

    private readonly InMemoryPharmacyStore store = new();
    private readonly IMailSender mails = Substitute.For<IMailSender>();
    private readonly FakeTimeProvider time = new(TestPharmacies.RegisteredAt.Value.AddMinutes(1));
    private readonly FakeLogger<InvitationMailRequestedV1Consumer> logger = new();

    [Fact]
    public void An_invitation_starts_at_its_first_version_and_each_renewal_is_the_next()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);

        pharmacy.Membership(membership).InvitationVersion.ShouldBe(InvitationVersion.First);
        pharmacy.RenewInvitation(membership, Now()).IsSuccess.ShouldBeTrue();
        pharmacy.RenewInvitation(membership, Now()).IsSuccess.ShouldBeTrue();

        pharmacy.Membership(membership).InvitationVersion.ShouldBe(InvitationVersion.From(3));
        pharmacy.Membership(membership).InvitationVersion.Value.ShouldBe(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_invitation_version_below_one_is_refused(int value) =>
        Should.Throw<GuardException>(() => InvitationVersion.From(value));

    [Fact]
    public void Recording_the_open_versions_mail_marks_it_and_raises_invitation_mailed()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        pharmacy.ClearDomainEvents();

        pharmacy.RecordInvitationMailed(membership, InvitationVersion.First).IsSuccess.ShouldBeTrue();

        pharmacy.Membership(membership).InvitationMailedFor.ShouldBe(InvitationVersion.First);
        var raised = pharmacy.DomainEvents.OfType<InvitationMailed>().ShouldHaveSingleItem();
        raised.Pharmacy.ShouldBe(pharmacy.Id);
        raised.Membership.ShouldBe(membership);
        raised.Version.ShouldBe(InvitationVersion.First);
    }

    [Theory]
    [InlineData("renewed")]
    [InlineData("revoked")]
    [InlineData("unknown")]
    public void A_mail_is_recorded_only_for_the_open_version(string which)
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        var target = membership;
        switch (which)
        {
            case "renewed":
                pharmacy.RenewInvitation(membership, Now()).IsSuccess.ShouldBeTrue();
                break;
            case "revoked":
                pharmacy.RevokeInvitation(membership).IsSuccess.ShouldBeTrue();
                break;
            default:
                target = MembershipId.New();
                break;
        }

        pharmacy.ClearDomainEvents();

        pharmacy.RecordInvitationMailed(target, InvitationVersion.First).Error.ShouldBeOfType<NoOpenInvitation>();

        pharmacy.Membership(membership).InvitationMailedFor.ShouldBeNull();
        pharmacy.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sent_mail_whose_mark_loses_its_race_is_logged_and_not_thrown()
    {
        var pharmacy = TestPharmacies.Register(Owner, "Apotheke am Markt");
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        pharmacy.Stored(store);
        store.LoseNextCommit();

        // Awaited directly: a throw here would make Wolverine mail the version a second time.
        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        await mails.Received(1).SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
        var warning = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain(membership.Value.ToString(), Case.Sensitive);
    }

    /// <summary>
    /// Once the send has returned the mail is out: a record commit that throws (a dropped database) is logged and
    /// swallowed, since a throw would make Wolverine mail the version a second time (review O1).
    /// </summary>
    [Fact]
    public async Task A_sent_mail_whose_mark_commit_throws_is_logged_and_not_thrown()
    {
        var pharmacy = TestPharmacies.Register(Owner, "Apotheke am Markt");
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        pharmacy.Stored(store);
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // The link's commit is done; the database goes away before the mark's commit.
            store.FailNextCommit(new IOException("The connection to the database was dropped."));
            return Task.CompletedTask;
        });

        // Awaited directly: a throw here would make Wolverine mail the version a second time.
        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        await mails.Received(1).SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
        var warning = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain(membership.Value.ToString(), Case.Sensitive);
    }

    [Fact]
    public async Task A_sent_mail_whose_invitation_was_renewed_before_its_mark_is_logged_and_not_thrown()
    {
        var pharmacy = TestPharmacies.Register(Owner, "Apotheke am Markt");
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        pharmacy.Stored(store);
        mails.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // The renewal lands on the consumer's own copy between the send and the mark, so the mark is refused.
            pharmacy.RenewInvitation(membership, Now()).IsSuccess.ShouldBeTrue();
            return Task.CompletedTask;
        });

        await Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken);

        pharmacy.Membership(membership).InvitationMailedFor.ShouldBeNull();
        store.Commits.ShouldBe(1, "the refused mark was committed");
        logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_link_is_issued_with_a_token_whose_hash_no_other_link_has()
    {
        var pharmacy = TestPharmacies.Register(Owner);
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        var token = InvitationToken.From(RandomNumberGenerator.GetHexString(64));
        pharmacy.IssueInvitationLink(membership, token, Now()).IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);

        var found = await store.LoadByInvitationAsync(InvitationTokenHash.Of(token), TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue();
        found.Value.Membership(membership).InvitationLinks.ShouldHaveSingleItem().Hash.ShouldBe(InvitationTokenHash.Of(token));
    }

    private static FrontendLinks Links() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Frontend:BaseUrl", "https://app.example")])
        .Build());

    private Timestamp Now() => Timestamp.From(time.GetUtcNow());

    private InvitationMailRequestedV1Consumer Consumer() => new(store, store, mails, Links(), time, logger);
}
