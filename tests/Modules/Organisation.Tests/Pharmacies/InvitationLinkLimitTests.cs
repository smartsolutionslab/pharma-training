using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// One invitation version holds at most five links, so a retry loop cannot mint links without limit. A sixth is refused
/// and the set stays as it was; the consumer at the cap mails nothing, issues no link and throws, so the message
/// dead-letters and stays visible: the invitee already holds the earlier links, and a renewal clears the set.
/// </summary>
public class InvitationLinkLimitTests
{
    private const int Cap = 5;

    private const string LimitReached = "organisation.invitation-link-limit-reached";

    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());
    private static readonly EmailAddress Anna = EmailAddress.From("anna@example.org");

    private readonly InMemoryPharmacyStore store = new();
    private readonly IMailSender mails = Substitute.For<IMailSender>();
    private readonly FakeTimeProvider time = new(TestPharmacies.RegisteredAt.Value.AddMinutes(1));
    private readonly FakeLogger<InvitationMailRequestedV1Consumer> logger = new();

    [Fact]
    public void A_sixth_link_for_one_invitation_version_is_refused_and_the_links_issued_before_still_work()
    {
        var pharmacy = TestPharmacies.Register(Owner, "Apotheke am Markt");
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        List<InvitationToken> issued = [];
        for (var each = 0; each < Cap; each++)
        {
            var token = Token();
            pharmacy.IssueInvitationLink(membership, token, Now()).IsSuccess.ShouldBeTrue($"link {each + 1} of {Cap} was refused");
            issued.Add(token);
        }

        pharmacy.ClearDomainEvents();
        var sixth = Token();

        var refused = pharmacy.IssueInvitationLink(membership, sixth, Now());

        refused.IsSuccess.ShouldBeFalse("a sixth link was issued for one invitation version");
        refused.Error.Code.ShouldBe(LimitReached);
        pharmacy.DomainEvents.ShouldBeEmpty();
        Accepts(pharmacy, sixth).ShouldBeFalse("the refused sixth link opens the invitation");
        Accepts(pharmacy, issued[0]).ShouldBeTrue("the first link stopped working when the sixth was refused");
    }

    /// <summary>
    /// At the cap the consumer mails nothing and issues no link, and throws, so the message dead-letters and stays
    /// visible instead of returning quietly (review B1, rule d). The exception names no personal data.
    /// </summary>
    [Fact]
    public async Task At_the_cap_the_consumer_sends_nothing_issues_no_link_and_throws()
    {
        var pharmacy = TestPharmacies.Register(Owner, "Apotheke am Markt");
        var membership = TestPharmacies.Invite(pharmacy, Anna.Value);
        for (var each = 0; each < Cap; each++)
        {
            pharmacy.IssueInvitationLink(membership, Token(), Now()).IsSuccess.ShouldBeTrue($"link {each + 1} of {Cap} was refused");
        }

        pharmacy.Stored(store);

        var thrown = await Should.ThrowAsync<Exception>(
            () => Consumer().HandleAsync(new InvitationMailRequestedV1(membership.Value, pharmacy.Id.Value), TestContext.Current.CancellationToken),
            "the consumer returned quietly at the cap, so the message is neither retried nor dead-lettered");

        await mails.DidNotReceive().SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>());
        store.Commits.ShouldBe(0);
        pharmacy.Membership(membership).InvitationLinks.Count.ShouldBe(Cap, "a link was issued at the cap");
        thrown.ShouldNotBeOfType<GuardException>();
        thrown.Message.ShouldNotContain(Anna.Value, Case.Insensitive);
        thrown.Message.ShouldNotContain("Apotheke am Markt", Case.Insensitive);
        thrown.InnerException.ShouldBeNull();
    }

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));

    private static FrontendLinks Links() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Frontend:BaseUrl", "https://app.example")])
        .Build());

    private Timestamp Now() => Timestamp.From(time.GetUtcNow());

    private bool Accepts(Pharmacy pharmacy, InvitationToken token) =>
        pharmacy.AcceptInvitation(token, PersonId.From(Guid.CreateVersion7()), DisplayName.From("Anna Berger"), Anna, Now()).IsSuccess;

    private InvitationMailRequestedV1Consumer Consumer() => new(store, store, mails, Links(), time, logger);
}
