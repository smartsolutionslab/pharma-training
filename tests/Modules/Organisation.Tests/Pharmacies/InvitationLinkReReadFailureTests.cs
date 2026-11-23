using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// The re-read after the link's commit sits inside the withdraw path (#193's review, follow-up 1): when it fails, the
/// mail was never sent, so the link this run committed is taken back and the failure goes on, like a send failure that
/// cannot have delivered. Otherwise every such failure leaves a link nobody was mailed, the version reaches the cap, and
/// the message dead-letters with nothing sent.
/// </summary>
public class InvitationLinkReReadFailureTests
{
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Anna = EmailAddress.From("anna@example.org");

    private readonly InMemoryPharmacyStore store = new();
    private readonly IMailSender mails = Substitute.For<IMailSender>();
    private readonly FakeTimeProvider time = new(TestPharmacies.RegisteredAt.Value.AddMinutes(1));
    private readonly FakeLogger<InvitationMailRequestedV1Consumer> logger = new();

    [Fact]
    public async Task A_re_read_that_fails_after_the_links_commit_leaves_the_link_set_as_it_was_and_still_throws()
    {
        var (pharmacy, membership) = Invited();
        var dropped = new TimeoutException("The database did not answer the re-read.");
        store.OnCommit(() => store.FailNextFind(dropped));

        var thrown = await Should.ThrowAsync<TimeoutException>(() => Consumer().HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(dropped);
        Stored(pharmacy, membership).InvitationLinks.ShouldBeEmpty("a link whose mail was never sent was left behind after the re-read failed");
        store.Commits.ShouldBe(2, "the link's withdrawal was not committed after the re-read failed");
        await mails.DidNotReceive().SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Re_reads_that_fail_on_every_attempt_never_reach_the_cap_so_every_attempt_throws_the_re_reads_failure()
    {
        var (pharmacy, membership) = Invited();
        var dropped = new TimeoutException("The database did not answer the re-read.");
        store.OnCommit(() => store.FailNextFind(dropped));

        for (var attempt = 1; attempt <= Pharmacy.MaxInvitationLinksPerVersion + 1; attempt++)
        {
            var thrown = await Should.ThrowAsync<Exception>(
                () => Consumer().HandleAsync(Request(pharmacy, membership), TestContext.Current.CancellationToken),
                $"attempt {attempt} did not fail");
            thrown.ShouldBeSameAs(dropped, $"attempt {attempt} threw {thrown.GetType().Name} ({thrown.Message}) instead of the re-read's failure");
        }

        Stored(pharmacy, membership).InvitationLinks.ShouldBeEmpty();
        await mails.DidNotReceive().SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
        logger.Collector.GetSnapshot().ShouldNotContain(record => record.Message.Contains("invitation-link-limit-reached", StringComparison.Ordinal));
    }

    private static InvitationMailRequestedV1 Request(Pharmacy pharmacy, MembershipId membership) => new(membership.Value, pharmacy.Id.Value);

    private static FrontendLinks Links() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Frontend:BaseUrl", "https://app.example")])
        .Build());

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
